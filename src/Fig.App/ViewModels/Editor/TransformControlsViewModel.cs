using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// Controls for the selected Transform effect (zoom, pan, rotate). The effect covers the whole clip.
/// While <see cref="IsAnimated"/> is off, the frame you set is the same everywhere. Turn animating on
/// (the stopwatch) and changes at the playhead become keyframes; values hold before the first and
/// after the last keyframe.
/// </summary>
public sealed partial class TransformControlsViewModel : ViewModelBase, Fig.App.Controls.IPreviewOverlayModel
{
    private readonly PropertiesViewModel _host;
    private readonly EditorViewModel _editor;
    private bool _refreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private bool _effectsTabOpen;

    // the camera at the playhead
    [ObservableProperty] private double _centerX = 0.5;
    [ObservableProperty] private double _centerY = 0.5;
    [ObservableProperty] private double _zoom = 1;
    [ObservableProperty] private double _rotation;

    [ObservableProperty] private double _frameAspect = 16.0 / 9.0;

    [ObservableProperty] private string _clipLabel = "";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _zoomText = "1.00";
    [ObservableProperty] private string _rotationText = "0";
    [ObservableProperty] private string _limitLengthText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private bool _isOnKeyframe;
    [ObservableProperty] private int _easingIndex = 3;

    /// <summary>The stopwatch: changes at the playhead make keyframes. Off means one look for the whole clip.</summary>
    [ObservableProperty] private bool _isAnimated;

    /// <summary>The effect is limited to part of the clip (otherwise it covers all of it).</summary>
    [ObservableProperty] private bool _hasLimit;

    /// <summary>The effect's keyframes on the inspector's ruler (the same ruler works for any keyframes).</summary>
    public KeyframeLaneViewModel Lane { get; }

    /// <summary>Where the camera looks at each keyframe, in order (the path drawn on the preview).</summary>
    public IReadOnlyList<(double X, double Y)> Path { get; private set; } = Array.Empty<(double, double)>();

    public string AspectText { get; private set; } = "16:9 · Locked";

    public bool ShowOverlay => IsActive && EffectsTabOpen && _host.ShowPreviewControls;

    public TransformControlsViewModel(PropertiesViewModel host, EditorViewModel editor)
    {
        _host = host;
        _editor = editor;
        Lane = new KeyframeLaneViewModel(editor);
    }

    /// <summary>The selected transform effect's keyframes, as the shared keyframe edits name them.</summary>
    private static KeyframeHostRef HostOf((IEffectOwner Clip, EffectInstance Effect) s) => KeyframeHostRef.OfEffect(s.Clip.Id, s.Effect.Id);

    public void NotifyControlsVisibilityChanged() => OnPropertyChanged(nameof(ShowOverlay));

    private (IEffectOwner Clip, EffectInstance Effect)? Selected()
        => _host.SelectedEffectInstance() is { Effect.TypeId: EffectCatalog.Transform } sel ? sel : null;

    private static List<double> TimesOf(EffectInstance fx)
        => fx.Keyframes.Values.SelectMany(tr => tr.Select(k => Math.Round(k.TimeSec, 6))).Distinct().OrderBy(t => t).ToList();

    /// <summary>Clip-relative time being edited: the playhead, snapped onto a keyframe when it is on one.</summary>
    private double EditTime(IEffectOwner clip, EffectInstance fx)
    {
        var t = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, clip.DurSec);
        var tolerance = 0.5 / Math.Max(1, _editor.Editor.Document.Rate.Fps);
        foreach (var k in TimesOf(fx))
            if (Math.Abs(k - t) <= tolerance)
                return k;
        return t;
    }

    /// <summary>Re-reads everything from the selected effect (called whenever the inspector refreshes or the playhead moves).</summary>
    public void Refresh()
    {
        var sel = Selected();
        _refreshing = true;
        try
        {
            IsActive = sel is not null;
            EffectsTabOpen = _host.IsOpen(Fig.Core.Timeline.Facet.Effects);
            _host.NotifyGenericParamsChanged();
            _host.NotifyPreviewControlsChanged();
            if (sel is not { } s)
            {
                Lane.Refresh(null, 1);
                return;
            }

            var (clip, fx) = s;
            var times = TimesOf(fx);
            var t = EditTime(clip, fx);
            var view = TransformMath.ViewAt(fx, t);
            var rate = _editor.Editor.Document.Rate;

            CenterX = view.CenterX;
            CenterY = view.CenterY;
            Zoom = view.Zoom;
            Rotation = view.RotationDegrees;
            ZoomText = $"{view.Zoom:0.00}";
            RotationText = $"{view.RotationDegrees:0.#}";
            IsAnimated = times.Count > 0;
            IsOnKeyframe = times.Any(k => Math.Abs(k - t) < 1e-6);
            TimeText = Timecode.Format(clip.StartSec + t, rate);
            ClipLabel = clip is Track track ? $"Track · {track.Name ?? "Video"}" : $"Selected clip · {_host.SelectedClipFileName ?? "clip"}";

            HasLimit = fx.HasRange;
            LimitLengthText = fx.HasRange ? $"{fx.DurSec:0.0} seconds" : "";
            Lane.Refresh(HostOf(s), clip.DurSec, fx.HasRange ? fx.StartSec : null,
                fx.HasRange ? fx.StartSec + fx.DurSec : null, _host.SelectedKeyframeSec);

            Path = times.Select(k => TransformMath.ViewAt(fx, k)).Select(v => (v.CenterX, v.CenterY)).ToList();
            OnPropertyChanged(nameof(Path));

            EasingIndex = (int)(_editor.Editor.FindKeyframeHost(HostOf(s))?.EasingAt(t) ?? KeyframeEasing.Linear);

            Summary = times.Count switch
            {
                0 => "Not animated",
                1 => "1 keyframe",
                _ => $"{times.Count} keyframes",
            };
            Hint = !IsAnimated
                ? "The same look throughout the effect. Press the stopwatch to animate it."
                : IsOnKeyframe
                    ? "Editing this keyframe. Drag the frame or change a value."
                    : "Move the playhead and drag the frame on the preview to add a keyframe there.";

            FrameAspect = _editor.Preview.CanvasAspect;
            AspectText = DescribeAspect(FrameAspect) + " · Locked";
            OnPropertyChanged(nameof(AspectText));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string DescribeAspect(double r)
    {
        (double Ratio, string Name)[] known =
            [(16.0 / 9, "16:9"), (4.0 / 3, "4:3"), (9.0 / 16, "9:16"), (1, "1:1"), (21.0 / 9, "21:9"), (3.0 / 2, "3:2")];
        foreach (var (ratio, name) in known)
            if (Math.Abs(r - ratio) < 0.02)
                return name;
        return $"{r:0.00}:1";
    }

    // ---- camera edits ---------------------------------------------------------------------

    /// <summary>Sets the camera: a keyframe at the playhead while animated, otherwise the one look for the whole clip.</summary>
    public void SetCamera(double centerX, double centerY, double zoom, double? rotation = null)
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        zoom = Math.Clamp(zoom, 1, 8);
        var half = 0.5 / zoom;
        centerX = Math.Clamp(centerX, half, 1 - half);
        centerY = Math.Clamp(centerY, half, 1 - half);
        var turn = Math.Clamp(rotation ?? Rotation, -180, 180);

        if (fx.Keyframes.Count == 0)
        {
            _host.WriteSelectedEffectParam("centerX", ParamValue.OfDouble(centerX));
            _host.WriteSelectedEffectParam("centerY", ParamValue.OfDouble(centerY));
            _host.WriteSelectedEffectParam("zoom", ParamValue.OfDouble(zoom));
            _host.WriteSelectedEffectParam("rotation", ParamValue.OfDouble(turn));
            return;
        }

        var t = EditTime(clip, fx);
        _editor.Editor.SetEffectKeyframe(clip.Id, fx.Id, t, new Dictionary<string, double>
        {
            ["centerX"] = centerX, ["centerY"] = centerY, ["zoom"] = zoom, ["rotation"] = turn,
        });
        _host.SelectKeyframe(t);
    }

    partial void OnZoomTextChanged(string value)
    {
        if (_refreshing)
            return;
        var text = value.Replace("×", "").Replace("x", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && zoom >= 1)
            SetCamera(CenterX, CenterY, zoom);
        else
            Refresh();
    }

    partial void OnRotationTextChanged(string value)
    {
        if (_refreshing)
            return;
        var text = value.Replace("°", "").Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
            SetCamera(CenterX, CenterY, Zoom, degrees);
        else
            Refresh();
    }

    partial void OnEasingIndexChanged(int value)
    {
        if (_refreshing || !IsOnKeyframe || Selected() is not { } s)
            return;
        var t = EditTime(s.Clip, s.Effect);
        _editor.Editor.SetKeyframeEasing(new KeyframeRef(HostOf(s), t),
            (KeyframeEasing)Math.Clamp(value, 0, Fig.App.Controls.KeyframeMenu.EasingNames.Count - 1));
    }

    /// <summary>Typing a time moves the keyframe there, or just jumps the playhead when it is not on one.</summary>
    partial void OnTimeTextChanged(string value)
    {
        if (_refreshing || Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var rate = _editor.Editor.Document.Rate;
        if (!Timecode.TryParse(value, rate, out var absolute))
        {
            Refresh();
            return;
        }
        var local = absolute - clip.StartSec;
        var t = EditTime(clip, fx);
        if (IsOnKeyframe)
            _editor.Editor.MoveEffectKeyframe(clip.Id, fx.Id, t, local, Guid.NewGuid().ToString("N"));
        _editor.SeekFromUser(absolute);
        Refresh();
    }

    // ---- limiting to part of the clip ------------------------------------------------------

    partial void OnHasLimitChanged(bool value)
    {
        if (_refreshing || Selected() is not { } s)
            return;
        var (clip, fx) = s;
        if (value && !fx.HasRange)
        {
            var from = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, Math.Max(0, clip.DurSec - 0.5));
            _editor.Editor.SetEffectRange(clip.Id, fx.Id, from, Math.Min(3, clip.DurSec - from));
        }
        else if (!value && fx.HasRange)
        {
            _editor.Editor.SetEffectRange(clip.Id, fx.Id, null, null);
        }
    }

    partial void OnLimitLengthTextChanged(string value)
    {
        if (_refreshing || Selected() is not { } s || !s.Effect.HasRange)
            return;
        var text = value.Replace("seconds", "", StringComparison.OrdinalIgnoreCase).Replace("s", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0.1)
        {
            Refresh();
            return;
        }
        _editor.Editor.SetEffectRange(s.Clip.Id, s.Effect.Id, s.Effect.StartSec ?? 0, seconds);
        Refresh();
    }

    // ---- animating -------------------------------------------------------------------------

    /// <summary>The stopwatch: records a keyframe at the playhead holding the current look. It never removes keyframes.</summary>
    [RelayCommand]
    private void ToggleAnimate()
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var t = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, clip.DurSec);
        if (_editor.Editor.RecordEffectKeyframe(clip.Id, fx.Id, t) is { } keyed)
            _host.SelectedKeyframeSec = keyed;
    }

    /// <summary>Removes every keyframe; the look at the playhead becomes the one look for the whole effect.</summary>
    [RelayCommand]
    private void ClearKeyframes()
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var t = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, clip.DurSec);
        _editor.Editor.SetEffectAnimated(clip.Id, fx.Id, false, t);
        _host.SelectedKeyframeSec = null;
    }

    /// <summary>The ◆ button: add a keyframe at the playhead, or remove the one already there.</summary>
    [RelayCommand]
    private void ToggleKeyframe()
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var t = EditTime(clip, fx);
        if (fx.Keyframes.Count == 0)
        {
            ToggleAnimate();
            return;
        }
        if (IsOnKeyframe)
        {
            _editor.Editor.RemoveEffectKeyframe(clip.Id, fx.Id, t);
            _host.SelectedKeyframeSec = null;
        }
        else if (_editor.Editor.AddEffectKeyframe(clip.Id, fx.Id, t) is { } added)
        {
            _host.SelectKeyframe(added);
        }
    }

    [RelayCommand]
    private void PreviousKeyframe() => StepKeyframe(forward: false);

    [RelayCommand]
    private void NextKeyframe() => StepKeyframe(forward: true);

    private void StepKeyframe(bool forward)
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var t = EditTime(clip, fx);
        var times = TimesOf(fx);
        var target = forward
            ? times.Where(k => k > t + 1e-6).Cast<double?>().FirstOrDefault()
            : times.Where(k => k < t - 1e-6).Cast<double?>().LastOrDefault();
        if (target is { } k2)
        {
            _host.SelectKeyframe(k2);
            _editor.SeekFromUser(clip.StartSec + k2);
        }
    }

    /// <summary>Keys the camera back to the whole picture at the playhead: the "zoom back out" step.</summary>
    [RelayCommand]
    private void BackToFullFrame()
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var t = EditTime(clip, fx);
        if (fx.Keyframes.Count == 0)
            _editor.Editor.SetEffectAnimated(clip.Id, fx.Id, true, t);
        _editor.Editor.SetEffectKeyframe(clip.Id, fx.Id, t, new Dictionary<string, double>
        {
            ["centerX"] = 0.5, ["centerY"] = 0.5, ["zoom"] = 1, ["rotation"] = Rotation,
        });
        _host.SelectKeyframe(t);
    }

    /// <summary>Plays the animated stretch (first keyframe to last), or the limited span, so the movement can be judged.</summary>
    [RelayCommand]
    private void PreviewMovement()
    {
        if (Selected() is not { } s)
            return;
        var (clip, fx) = s;
        var times = TimesOf(fx);
        var from = times.Count > 1 ? times[0] : (fx.StartSec ?? 0);
        var to = times.Count > 1 ? times[^1] : from + (fx.DurSec ?? clip.DurSec - from);
        _editor.PlayRange(clip.StartSec + Math.Max(0, from - 0.3), clip.StartSec + Math.Min(clip.DurSec, to + 0.3));
    }
}
