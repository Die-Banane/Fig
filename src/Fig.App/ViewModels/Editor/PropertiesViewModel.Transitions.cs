using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The Transition tab: the picked transition's duration, parameters and actions.
/// </summary>
public partial class PropertiesViewModel
{
    // transition
    [ObservableProperty] private string? _transitionTypeLabel;
    [ObservableProperty] private string? _transitionDescription;
    [ObservableProperty] private string? _transitionEditPoint;
    [ObservableProperty] private string? _transitionOutgoing;
    [ObservableProperty] private string? _transitionIncoming;
    [ObservableProperty] private string _transitionDurationText = "";
    [ObservableProperty] private string? _transitionSpanLabel;
    [ObservableProperty] private double _transitionDurationValue;
    [ObservableProperty] private double _transitionMaxDuration = 1;

    private void LoadTransition(string key)
    {
        if (_editor.Editor.GetTransition(key) is not { } transition)
            return;
        var displayName = TransitionCatalog.Find(transition.TypeId)?.DisplayName ?? transition.TypeId;

        _clipId = null;
        _transitionKey = transition.Key;
        TransitionTypeLabel = displayName;
        TransitionDescription = TransitionCatalog.Find(transition.TypeId)?.Description;
        TransitionEditPoint = Timecode.Format(transition.CutSec, _editor.Editor.Document.Rate);
        TransitionOutgoing = ClipFileName(transition.Left);
        TransitionIncoming = ClipFileName(transition.Right);
        Subtitle = $"{TransitionOutgoing} \u2192 {TransitionIncoming}";
        TransitionDurationValue = transition.DurationSec;
        TransitionDurationText = $"{transition.DurationSec:0.00} s";
        TransitionMaxDuration = Math.Max(transition.DurationSec, Math.Max(0.05,
            _editor.Editor.GetMaxTransitionDuration(transition.LeftClipId, transition.RightClipId)));
        TransitionSpanLabel = $"{FormatTime(transition.DurationSec)} across the cut";
        BuildTransitionParams(transition);
    }

    partial void OnTransitionDurationValueChanged(double value)
    {
        if (_suppressApply || _transitionKey is null || !Has(Facet.Transition))
            return;
        var transition = _editor.Editor.GetTransition(_transitionKey);
        if (transition is null)
            return;
        var clamped = Math.Clamp(value, 0, TransitionMaxDuration);
        _editor.Editor.SetTransitionDuration(transition.LeftClipId, transition.RightClipId, clamped);
        if (Math.Abs(clamped - value) > 1e-6)
        {
            _suppressApply = true;
            TransitionDurationValue = clamped;
            _suppressApply = false;
        }
        TransitionSpanLabel = $"{FormatTime(clamped)} across the cut";
        TransitionDurationText = $"{clamped:0.00} s";
        _editor.Preview.RefreshFrame();
    }

    /// <summary>Applies a typed duration such as "1.4" or "1.40 s".</summary>
    partial void OnTransitionDurationTextChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Transition))
            return;
        var text = value.Replace("s", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0 && Math.Abs(seconds - TransitionDurationValue) > 1e-6)
            TransitionDurationValue = seconds;
        else
            TransitionDurationText = $"{TransitionDurationValue:0.00} s";
    }

    private string ClipFileName(Clip clip)
        => clip switch
        {
            VideoClip v when _editor.MediaById.TryGetValue(v.SourceId, out var a) => a.FileName,
            AudioClip au when _editor.MediaById.TryGetValue(au.SourceId, out var a) => a.FileName,
            _ => "clip",
        };

    internal CutTransition? CurrentTransition()
        => Has(Facet.Transition) && _transitionKey is not null
            ? _editor.Editor.GetTransition(_transitionKey)
            : null;

    internal void WriteTransitionParam(string key, ParamValue value)
    {
        if (CurrentTransition() is not { } t)
            return;
        _editor.Editor.SetTransitionParam(t.LeftClipId, t.RightClipId, key, value);
        _editor.Preview.RefreshFrame();
    }

    /// <summary>Plays across the cut so the transition can be judged.</summary>
    [RelayCommand]
    private void PreviewTransition()
    {
        if (CurrentTransition() is not { } t)
            return;
        var half = t.DurationSec / 2;
        _editor.PlayRange(Math.Max(0, t.CutSec - half - 0.4), t.CutSec + half + 0.4);
    }

    /// <summary>Opens the transitions library so a different one can be dragged onto the cut.</summary>
    [RelayCommand]
    private void ReplaceTransition() => _editor.OpenTransitionsLibrary();

    /// <summary>Puts every control of the transition back to its default.</summary>
    [RelayCommand]
    private void ResetTransitionControls()
    {
        if (CurrentTransition() is not { } t || TransitionCatalog.Find(t.TypeId) is not { } entry)
            return;
        foreach (var def in entry.ParamSchema)
            _editor.Editor.SetTransitionParam(t.LeftClipId, t.RightClipId, def.Key, def.DefaultValue());
        _lastTransitionSignature = "";
        Refresh();
        _editor.Preview.RefreshFrame();
    }

    [RelayCommand]
    private void RemoveTransition()
    {
        if (_transitionKey is null || !Has(Facet.Transition))
            return;
        var transition = _editor.Editor.GetTransition(_transitionKey);
        if (transition is null)
            return;
        _editor.Editor.RemoveTransition(transition.LeftClipId, transition.RightClipId);
        _editor.Preview.RefreshFrame();
        Refresh();
    }

    private void BuildTransitionParams(CutTransition transition)
    {
        // Only rebuild when the transition structure (type / parameter set) actually changed;
        // slider drags re-enter via TimelineChanged on every tick and would otherwise replace
        // the editor mid-drag.
        var signature = TransitionSignature(transition);
        if (_lastTransitionSignature == signature)
            return;
        _lastTransitionSignature = signature;

        TransitionParams.Clear();
        var entry = TransitionCatalog.Find(transition.TypeId);
        if (entry is null)
            return;

        foreach (var def in entry.ParamSchema)
        {
            var key = def.Key;
            ParamValue Current()
                => transition.Left.TransitionOut?.Params.TryGetValue(key, out var v1) == true ? v1
                    : transition.Right.TransitionIn?.Params.TryGetValue(key, out var v2) == true ? v2
                    : def.DefaultValue();
            var editor = ParamEditorViewModel.For(def, new PlainParamTarget(Current,
                value => _editor.Editor.SetTransitionParam(transition.LeftClipId, transition.RightClipId, key, value)));
            editor.RefreshValue();
            TransitionParams.Add(editor);
        }
    }

    private static string TransitionSignature(CutTransition transition)
    {
        // Structural only (type + which params exist). Param values update through the
        // two-way slider bindings; including them would rebuild the editors mid-drag on
        // every TimelineChanged tick.
        var sb = new System.Text.StringBuilder();
        sb.Append(transition.TypeId).Append('|');
        var source = transition.Left.TransitionOut?.Params
                     ?? transition.Right.TransitionIn?.Params
                     ?? new Dictionary<string, ParamValue>();
        foreach (var key in source.Keys)
            sb.Append(key).Append(';');
        return sb.ToString();
    }
}
