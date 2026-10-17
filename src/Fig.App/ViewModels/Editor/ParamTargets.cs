using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// A keyframable value: an effect's parameter or a clip's property. At the playhead it reads its keyframes when it has
/// any, else its base value; writing sets the key there once it is animated, else the base value. Toggling and
/// stepping go through the editor's shared keyframe edits, so these behave like every other keyframe.
/// </summary>
public sealed class KeyedParamTarget(
    EditorViewModel editor, KeyframeHostRef host, string key, bool canKey,
    Func<ParamValue> readBase, Action<ParamValue> writeBase, bool writeHandlesKeys = false) : IParamTarget
{
    private KeyframeHost? Host => editor.Editor.FindKeyframeHost(host);

    private List<KeyframePoint>? Keys => Host?.Tracks.FirstOrDefault(t => t.Key == key).Value is { Count: > 0 } keys ? keys : null;

    /// <summary>The playhead in the host's own time.</summary>
    private double Local => Host is { } h ? Math.Clamp(editor.PlayheadTimeSec - h.OriginSec, 0, h.LimitSec) : 0;

    public bool CanKey => canKey;

    public ParamValue Read() => Keys is { } keys ? EffectPipeline.Evaluate(keys, Local) : readBase();

    public void Write(ParamValue value)
    {
        // a clip property's own write knows about keys (it keys every selected clip); an effect's is set here
        if (canKey && !writeHandlesKeys && Keys is not null)
            editor.Editor.SetKeyframe(host, key, Local, value, $"{key}@{Math.Round(Local, 4)}");
        else
            writeBase(value);
        editor.Preview.RefreshFrame();
    }

    public bool IsOnKeyframe => Keys?.Any(k => Math.Abs(k.TimeSec - Local) < 1e-3) == true;

    public int KeyframeCount => Keys?.Count ?? 0;

    public void ToggleKeyframe()
    {
        if (!canKey)
            return;
        var local = Local;
        if (IsOnKeyframe)
            editor.Editor.RemoveKeyframe(host, key, local);
        else
            editor.Editor.SetKeyframe(host, key, local, Read());
        editor.Properties.RefreshAutomation();
        editor.Preview.RefreshFrame();
    }

    public void StepKeyframe(bool forward)
    {
        if (Keys is not { } keys || Host is not { } h)
            return;
        var local = Local;
        var target = forward
            ? keys.Where(k => k.TimeSec > local + 1e-6).Select(k => (double?)k.TimeSec).FirstOrDefault()
            : keys.Where(k => k.TimeSec < local - 1e-6).Select(k => (double?)k.TimeSec).LastOrDefault();
        if (target is { } t)
            editor.SeekFromUser(h.OriginSec + t);
    }
}

/// <summary>A group of property editors in the inspector ("Compositing", "Crop"...), with a reset to their defaults.</summary>
public sealed partial class PropertyGroupViewModel(string title) : ViewModelBase
{
    public string Title { get; } = title;

    public ObservableCollection<ParamEditorViewModel> Editors { get; } = new();

    /// <summary>Hidden when none of its editors is.</summary>
    [ObservableProperty] private bool _isShown = true;

    public void Refresh()
    {
        foreach (var editor in Editors)
            editor.RefreshValue();
        IsShown = Editors.Any(e => e.IsShown);
    }

    [RelayCommand]
    private void Reset()
    {
        foreach (var editor in Editors)
            editor.Target.Write(editor.Def.DefaultValue());
        Refresh();
    }
}
