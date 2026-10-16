using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fig.App.ViewModels;

/// <summary>
/// J / K / L shuttle and keyboard stepping. Shuttle moves the playhead at a multiple of normal speed (either
/// way) and the audio follows it, like a jog wheel; a TourBox knob or any controller can be mapped to these
/// keys. Normal 1x playback stays on Space and the audio clock.
/// </summary>
public partial class EditorViewModel
{
    private const double MaxShuttleSpeed = 8;

    private DispatcherTimer? _shuttleTimer;
    private double _shuttleSpeed;
    private long _shuttleLastTicks;

    /// <summary>Whether moving the playhead by hand (drag, arrows, shuttle) plays the audio under it.</summary>
    [ObservableProperty]
    private bool _audioScrub = true;

    partial void OnAudioScrubChanged(bool value)
    {
        if (Playback is not null)
            Playback.AudioScrubEnabled = value;
        Remember(p => p.Playback.AudioScrub = value);
        StatusHint = value ? "Audio scrubbing on" : "Audio scrubbing off";
    }

    /// <summary>Signed shuttle speed (negative is reverse); 0 when not shuttling.</summary>
    public double ShuttleSpeed => _shuttleSpeed;

    [RelayCommand]
    private void ToggleAudioScrub() => AudioScrub = !AudioScrub;

    /// <summary>L: play forward; again for 2x, 4x, 8x. From reverse it returns to normal forward play.</summary>
    [RelayCommand]
    private async Task ShuttleForwardAsync()
    {
        if (Playback is null)
            return;
        if (_shuttleSpeed > 0)
        {
            SetShuttleSpeed(Math.Min(_shuttleSpeed * 2, MaxShuttleSpeed));
        }
        else if (_shuttleSpeed < 0)
        {
            StopShuttle();
            await TogglePlaybackAsync();
        }
        else if (Playback.IsPlaying)
        {
            await Playback.PauseAsync();
            IsPlaying = false;
            StartShuttle(2);
        }
        else
        {
            await TogglePlaybackAsync();
        }
    }

    /// <summary>J: play backward; again for 2x, 4x, 8x.</summary>
    [RelayCommand]
    private async Task ShuttleBackAsync()
    {
        if (Playback is null)
            return;
        if (_shuttleSpeed < 0)
        {
            SetShuttleSpeed(Math.Max(_shuttleSpeed * 2, -MaxShuttleSpeed));
            return;
        }
        if (Playback.IsPlaying)
        {
            await Playback.PauseAsync();
            IsPlaying = false;
        }
        StartShuttle(-1);
    }

    /// <summary>K: stop, whether playing or shuttling.</summary>
    [RelayCommand]
    private async Task ShuttleStopAsync()
    {
        StopShuttle();
        if (Playback is { IsPlaying: true })
        {
            await Playback.PauseAsync();
            IsPlaying = false;
        }
    }

    [RelayCommand]
    private void StepBackSecond() => SeekFromUser(Math.Max(0, PlayheadTimeSec - 1));

    [RelayCommand]
    private void StepForwardSecond() => SeekFromUser(PlayheadTimeSec + 1);

    private void StartShuttle(double speed)
    {
        _shuttleSpeed = speed;
        _shuttleLastTicks = Stopwatch.GetTimestamp();
        _shuttleTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnShuttleTick);
        _shuttleTimer.Start();
        AnnounceShuttle();
    }

    private void SetShuttleSpeed(double speed)
    {
        _shuttleSpeed = speed;
        AnnounceShuttle();
    }

    private void AnnounceShuttle()
    {
        OnPropertyChanged(nameof(ShuttleSpeed));
        StatusHint = _shuttleSpeed == 0
            ? "Click a clip to select it · Space plays · J K L shuttle"
            : $"Shuttle {(_shuttleSpeed < 0 ? "◀◀" : "▶▶")} {Math.Abs(_shuttleSpeed):0}×  ·  K stops";
    }

    private void StopShuttle()
    {
        if (_shuttleSpeed == 0)
            return;
        _shuttleTimer?.Stop();
        _shuttleSpeed = 0;
        AnnounceShuttle();
    }

    private void OnShuttleTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _shuttleLastTicks) / (double)Stopwatch.Frequency;
        _shuttleLastTicks = now;

        var next = PlayheadTimeSec + _shuttleSpeed * dt;
        var end = SequenceEndSec;
        var atEdge = next <= 0 || next >= end;
        MovePlayhead(Math.Clamp(next, 0, end), audible: true);
        if (atEdge)
            StopShuttle();
    }
}
