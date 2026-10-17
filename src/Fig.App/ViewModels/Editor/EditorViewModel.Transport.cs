using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.App.Views;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Templates;
using Fig.Core.Timeline;
using ProjectModel = Fig.Core.Project.Project;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The playhead and playback: seeking, playing a range, play / pause, jumping and frame stepping.
/// </summary>
public partial class EditorViewModel
{
    /// <summary>Timeline scrub or transport seek (not driven by playback clock).</summary>
    public void SeekFromUser(double sec)
    {
        StopShuttle();
        MovePlayhead(sec, audible: true);
    }

    /// <summary>Moves the playhead; while stopped, scrubbing also plays the audio under it.</summary>
    private void MovePlayhead(double sec, bool audible)
    {
        _stopAtSec = null;
        PlayheadTimeSec = Math.Max(0, sec);
        Playback?.Seek(PlayheadTimeSec);
        if (audible && AudioScrub)
            Playback?.ScrubTo(PlayheadTimeSec);
        Preview.PlayheadSec = PlayheadTimeSec;
    }

    private double? _stopAtSec;

    /// <summary>Plays from <paramref name="startSec"/> and pauses when it reaches <paramref name="endSec"/>.</summary>
    public async void PlayRange(double startSec, double endSec)
    {
        if (Playback is null)
            return;
        if (Playback.IsPlaying)
            await Playback.PauseAsync();
        _stopAtSec = null;
        MovePlayhead(startSec, audible: false);
        _stopAtSec = endSec;
        IsPlaying = true;
        Preview.PreRollPlayback();
        await Playback.PlayAsync();
        IsPlaying = Playback.IsPlaying;
    }

    /// <summary>Audio master clock position during playback.</summary>
    public void NotifyPlaybackPosition(double sec)
    {
        if (_stopAtSec is { } stop && sec >= stop)
        {
            _stopAtSec = null;
            _ = Playback?.PauseAsync();
        }
        PlayheadTimeSec = Math.Max(0, sec);
        Preview.PlayheadSec = PlayheadTimeSec;
        IsPlaying = Playback?.IsPlaying ?? false;
    }

    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        if (Playback is null)
            return;
        _stopAtSec = null;
        StopShuttle();
        if (Playback.IsPlaying)
        {
            await Playback.PauseAsync();
        }
        else
        {
            // Flag as playing first so the decode worker fills the playback FIFO, then
            // pre-roll the first video frame before the audio clock starts. This removes
            // the initial black/stale preview stall while playback seeks + decodes the
            // first frame mid-clip.
            IsPlaying = true;
            Preview.PreRollPlayback();
            await Playback.PlayAsync();
        }
        IsPlaying = Playback.IsPlaying;
    }

    [RelayCommand]
    private async Task JumpToStartAsync()
    {
        if (Playback is { IsPlaying: true })
            await Playback.PauseAsync();
        if (Playback is not null)
            await Playback.SeekAsync(0);
        PlayheadTimeSec = 0;
        Preview.PlayheadSec = 0;
        IsPlaying = false;
    }

    [RelayCommand]
    private async Task JumpToEndAsync()
    {
        if (Playback is { IsPlaying: true })
            await Playback.PauseAsync();
        if (Playback is not null)
            await Playback.SeekAsync(SequenceEndSec);
        PlayheadTimeSec = SequenceEndSec;
        Preview.PlayheadSec = SequenceEndSec;
        IsPlaying = false;
    }

    [RelayCommand]
    private void StepBackFrame() => SeekFromUser(Math.Max(0, PlayheadTimeSec - FrameDurationSec));

    [RelayCommand]
    private void StepForwardFrame() => SeekFromUser(PlayheadTimeSec + FrameDurationSec);
}
