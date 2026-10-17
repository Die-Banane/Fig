using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The Effects tab: the effect stack of the selected clip or track, and the picked effect's parameter editors.
/// </summary>
public partial class PropertiesViewModel
{
    // effect stack (clip context)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGenericParams))]
    [NotifyPropertyChangedFor(nameof(IsSelectedEffectCustom))]
    private EffectItemViewModel? _selectedEffect;
    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();
    public ObservableCollection<ParamEditorViewModel> EffectParams { get; } = new();
    public ObservableCollection<ParamEditorViewModel> TransitionParams { get; } = new();

    public bool HasEffects => Effects.Count > 0;

    /// <summary>The generic parameter list is hidden for effects that have their own panel.</summary>
    public bool ShowGenericParams => SelectedEffect is not null && !Transform.IsActive;

    /// <summary>True when the selected effect is one the user built (so it can be opened in the graph editor).</summary>
    public bool IsSelectedEffectCustom
        => SelectedEffect is { } effect && EffectCatalog.Find(effect.TypeId)?.IsCustom == true;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void EditSelectedGraph()
    {
        if (SelectedEffect is { } effect)
            _editor.EditGraphEffect(effect.TypeId);
    }
    public bool NoEffects => Effects.Count == 0;

    private string? _lastEffectsClipId;
    private string _lastEffectsSignature = "";
    private string _lastTransitionSignature = "";
    private string? _selectedEffectId;
    private string? _lastBuiltEffectId;

    /// <summary>Turns every effect on the clip off, or back on when they are all already off.</summary>
    [RelayCommand]
    private void BypassAllEffects()
    {
        if (_clipId is null || _editor.Editor.FindEffectOwner(_clipId) is not { } clip || clip.Effects.Count == 0)
            return;
        var turnOff = clip.Effects.Any(e => e.Enabled);
        foreach (var effect in clip.Effects.Where(e => e.Enabled == turnOff).ToList())
            _editor.Editor.ToggleEffect(clip.Id, effect.Id);
        _lastEffectsClipId = null;
        Refresh();
        _editor.Preview.RefreshFrame();
    }

    // ---- effect stack + schema-driven param editors ----

    /// <summary>Shows the owner's effect stack, with the picked effect (when an effect bar was picked) open.</summary>
    private void LoadEffects(IEffectOwner owner, bool focusPicked)
    {
        BuildEffectStack(owner);
        if (focusPicked && _editor.Editor.Selection.SelectedEffect is { } picked)
            SelectedEffect = Effects.FirstOrDefault(item => item.EffectId == picked.EffectId);
    }

    private void BuildEffectStack(IEffectOwner clip)
    {
        // Same clip as last time: only rebuild the list when the stack actually changed
        // (add/remove/reorder/toggle). The signature covers ids + order + enabled state;
        // otherwise just refresh the evaluated values of the existing param editors.
        var signature = EffectSignature(clip);
        if (_lastEffectsClipId == clip.Id && _lastEffectsSignature == signature)
        {
            RefreshParams();
            return;
        }

        _lastEffectsClipId = clip.Id;
        _lastEffectsSignature = signature;
        Effects.Clear();
        foreach (var effect in clip.Effects)
        {
            var entry = EffectCatalog.Find(effect.TypeId);
            Effects.Add(new EffectItemViewModel(
                effect.Id,
                effect.TypeId,
                entry?.DisplayName ?? effect.TypeId,
                entry?.Icon ?? "wand-sparkles",
                effect.Enabled,
                ToggleEffect,
                RemoveEffect));
        }

        SelectedEffect = Effects.FirstOrDefault();
        _selectedEffectId = SelectedEffect?.EffectId;
        RebuildEffectParams();
    }

    private static string EffectSignature(IEffectOwner clip)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var effect in clip.Effects)
        {
            sb.Append(effect.Id).Append(':').Append(effect.Order).Append(':')
              .Append(effect.Enabled ? 1 : 0).Append(';');
        }
        return sb.ToString();
    }

    partial void OnSelectedSectionChanged(InspectorSection? value) => Transform?.Refresh();

    partial void OnSelectedEffectChanged(EffectItemViewModel? value)
    {
        var nextId = value?.EffectId;
        if (nextId == _selectedEffectId && nextId == _lastBuiltEffectId)
            return;
        _selectedEffectId = nextId;
        SelectedKeyframeSec = null;
        RebuildEffectParams();
        Transform?.Refresh();
    }

    private void ToggleEffect(EffectItemViewModel item)
    {
        if (_clipId is null)
            return;
        _editor.Editor.ToggleEffect(_clipId, item.EffectId);
        item.IsEnabled = !item.IsEnabled;
    }

    private void RemoveEffect(EffectItemViewModel item)
    {
        if (_clipId is null)
            return;
        _editor.Editor.RemoveEffect(_clipId, item.EffectId);
        _lastEffectsClipId = null; // force the stack to rebuild on the next refresh
    }

    private void RebuildEffectParams()
    {
        EffectParams.Clear();
        _lastBuiltEffectId = _selectedEffectId;
        if (_clipId is null || _selectedEffectId is null)
            return;
        var owner = _editor.Editor.FindEffectOwner(_clipId);
        var effect = owner is null ? null : FindEffect(owner, _selectedEffectId);
        if (effect is null || EffectCatalog.Find(effect.TypeId) is not { } entry)
            return;

        var (clipId, effectId) = (_clipId, effect.Id);
        foreach (var def in entry.ParamSchema)
        {
            var key = def.Key;
            // an effect's parameter: the same keyed value as a clip's property
            var target = new KeyedParamTarget(_editor, KeyframeHostRef.OfEffect(clipId, effectId), key, canKey: true,
                readBase: () => FindEffectById(clipId, effectId)?.Params.TryGetValue(key, out var v) == true ? v : def.DefaultValue(),
                writeBase: value => _editor.Editor.SetEffectParam(clipId, effectId, key, value));
            EffectParams.Add(ParamEditorViewModel.For(def, target));
        }
        RefreshParams();
    }

    private EffectInstance? FindEffectById(string ownerId, string effectId)
        => _editor.Editor.FindEffectOwner(ownerId) is { } owner ? FindEffect(owner, effectId) : null;

    private void RefreshParams()
    {
        foreach (var editor in EffectParams)
            editor.RefreshValue();
    }

    /// <summary>The clip and effect currently selected in the Effects tab, if any.</summary>
    internal (IEffectOwner Clip, EffectInstance Effect)? SelectedEffectInstance()
    {
        if (_clipId is null || _selectedEffectId is null || _editor.Editor.FindEffectOwner(_clipId) is not { } clip
            || FindEffect(clip, _selectedEffectId) is not { } effect)
            return null;
        return (clip, effect);
    }

    /// <summary>The keyframe (clip-relative seconds) picked on the selected effect's bar, if any.</summary>
    [ObservableProperty]
    private double? _selectedKeyframeSec;

    public void SelectKeyframe(double sec) => SelectedKeyframeSec = sec;

    partial void OnSelectedKeyframeSecChanged(double? value) => Transform?.Refresh();

    internal string? SelectedClipFileName => FileName;

    internal void NotifyGenericParamsChanged() => OnPropertyChanged(nameof(ShowGenericParams));

    internal void WriteSelectedEffectParam(string key, ParamValue value)
    {
        if (SelectedEffectInstance() is { } sel)
            WriteEffectParam(sel.Clip.Id, sel.Effect.Id, key, value);
    }

    private void WriteEffectParam(string clipId, string effectId, string key, ParamValue value)
    {
        var clip = _editor.Editor.FindEffectOwner(clipId);
        if (clip is null)
            return;
        var effect = FindEffect(clip, effectId);
        var keyframed = effect?.Keyframes.TryGetValue(key, out var track) == true && track.Count > 0;
        if (keyframed)
            _editor.Editor.SetKeyframe(clipId, effectId, key, Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec), value);
        else
            _editor.Editor.SetEffectParam(clipId, effectId, key, value);
    }

    private static EffectInstance? FindEffect(IEffectOwner clip, string effectId)
    {
        foreach (var effect in clip.Effects)
            if (effect.Id == effectId)
                return effect;
        return null;
    }

    /// <summary>Picks an effect bar (a bar of a clip or a track) and opens the Effects tab on it.</summary>
    public void SelectEffect(string clipId, string effectId)
    {
        var editor = _editor.Editor;
        editor.Selection.SelectEffect(clipId, effectId);
        Refresh();
        foreach (var item in Effects)
            if (item.EffectId == effectId)
            {
                SelectedEffect = item;
                break;
            }
        OpenSection(Facet.Effects);
    }
}
