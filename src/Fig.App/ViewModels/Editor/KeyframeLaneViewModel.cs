using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// What the inspector's keyframe ruler shows and does, for any keyframe host: its keys along the span it covers,
/// the playhead, the picked keyframe. Every action is the editor's shared keyframe edit, and picking goes through
/// <see cref="EditorViewModel.PickKeyframe"/>, so a keyframe picked here is picked on the timeline too.
/// </summary>
public sealed partial class KeyframeLaneViewModel : ViewModelBase
{
    public KeyframeLaneViewModel(EditorViewModel editor) => Editor = editor;

    public EditorViewModel Editor { get; }

    /// <summary>The keyframes shown, or null when there is nothing to show.</summary>
    [ObservableProperty] private KeyframeHostRef? _host;

    /// <summary>The span the ruler covers (seconds from the host's start).</summary>
    [ObservableProperty] private double _clipDurationSec = 1;
    [ObservableProperty] private double _playheadLocal;
    [ObservableProperty] private double? _selectedKeyframe;
    [ObservableProperty] private double? _limitStart;
    [ObservableProperty] private double? _limitEnd;

    public IReadOnlyList<double> KeyTimes { get; private set; } = Array.Empty<double>();

    /// <summary>
    /// Re-reads the host's keys. <paramref name="spanSec"/>: how much the ruler covers; <paramref name="pickedSec"/>:
    /// a keyframe picked by its owner's own controls (an effect's), when the timeline hasn't picked one.
    /// </summary>
    public void Refresh(KeyframeHostRef? host, double spanSec, double? limitStart = null, double? limitEnd = null, double? pickedSec = null)
    {
        Host = host;
        var found = host is { } h ? Editor.Editor.FindKeyframeHost(h) : null;
        KeyTimes = found?.Times ?? Array.Empty<double>();
        OnPropertyChanged(nameof(KeyTimes));
        ClipDurationSec = Math.Max(0.01, spanSec);
        PlayheadLocal = Math.Clamp(Editor.PlayheadTimeSec - (found?.OriginSec ?? 0), 0, ClipDurationSec);
        LimitStart = limitStart;
        LimitEnd = limitEnd;
        var picked = Editor.Editor.Selection.Keyframe is { } k && k.Host == host ? k.TimeSec : pickedSec;
        SelectedKeyframe = picked is { } p && KeyTimes.Any(t => Math.Abs(t - p) < 1e-4) ? p : null;
    }

    private KeyframeRef? At(double localSec) => Host is { } h ? new KeyframeRef(h, localSec) : null;

    /// <summary>Moves the playhead to a time on the ruler.</summary>
    public void Seek(double localSec)
    {
        if (Host is { } h && Editor.Editor.FindKeyframeHost(h) is { } found)
            Editor.SeekFromUser(found.OriginSec + Math.Clamp(localSec, 0, ClipDurationSec));
    }

    public void Select(double localSec)
    {
        if (At(localSec) is { } keyframe)
            Editor.PickKeyframe(keyframe);
    }

    /// <summary>One step of dragging a keyframe; every step of one drag shares <paramref name="gesture"/>.</summary>
    public void Move(double fromSec, double toSec, string gesture)
    {
        if (At(fromSec) is not { } keyframe)
            return;
        Editor.Editor.MoveKeyframes(keyframe, toSec, gesture);
        Editor.Editor.Selection.MoveKeyframe(toSec);
        if (keyframe.Host.EffectId is not null)
            Editor.Properties.SelectKeyframe(toSec);
    }

    /// <summary>Adds a keyframe holding the current look there, and picks it.</summary>
    public void Add(double localSec)
    {
        if (Host is { } h && Editor.Editor.AddKeyframe(h, localSec) is { } added)
            Editor.PickKeyframe(new KeyframeRef(h, added));
    }

    /// <summary>Frame-snaps a time on the ruler, for dragging.</summary>
    public double Snap(double localSec) => Editor.Editor.SnapTime(localSec);
}
