using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// Beat markers as a short session: analyze the selected audio clip once, tune the sensitivity
/// while candidate markers follow the slider on the timeline, then apply (one undo step) or cancel.
/// </summary>
public sealed partial class BeatMarkersViewModel : ViewModelBase
{
    private readonly EditorViewModel _host;

    // Everything the analysis depended on; if any of it changes the preview no longer matches the audio.
    private sealed record AnalyzedClip(TimelineEditor Editor, Clip Clip, string SourceId, double SrcIn,
        double SrcOut, double Dur, double Speed, double PlayRate);

    private AnalyzedClip? _session;
    private BeatAnalysis? _analysis;
    private int _generation;

    [ObservableProperty]
    private double _sensitivity = 50;

    [ObservableProperty]
    private bool _kicksOnly;

    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [ObservableProperty]
    private bool _isAnalyzing;

    [NotifyPropertyChangedFor(nameof(ButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [ObservableProperty]
    private bool _hasPreview;

    [NotifyPropertyChangedFor(nameof(ButtonLabel))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [ObservableProperty]
    private string _status = "";

    [NotifyPropertyChangedFor(nameof(ButtonLabel))]
    [ObservableProperty]
    private int _markerCount;

    public BeatMarkersViewModel(EditorViewModel host) => _host = host;

    public bool CanAnalyze => !IsAnalyzing;
    public bool IsIdle => !HasPreview;
    public bool HasStatus => Status.Length > 0;
    public string ButtonLabel => HasPreview ? $"Beats · {MarkerCount}" : "Beats";

    partial void OnSensitivityChanged(double value) => RefreshPreview();

    partial void OnKicksOnlyChanged(bool value) => RefreshPreview();

    /// <summary>Decodes and analyzes the selected audio clip, then shows candidate markers on it.</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task Analyze()
    {
        var editor = _host.Editor;
        var selected = editor.Selection.SelectedClipIds;
        // an audio clip, or a video clip playing its own sound
        var clip = editor.Document.Tracks.SelectMany(track => track.Clips)
            .FirstOrDefault(c => c is AudioClip or VideoClip { PlaysAudio: true } && selected.Contains(c.Id));
        if (clip is null)
        {
            Fail("Select a clip with sound first.");
            return;
        }
        var asset = _host.FindAsset(clip.MediaId!);
        if (asset is null || asset.Offline)
        {
            Fail("The source audio is unavailable.");
            return;
        }

        // Match the source-time advance used by AudioMixer.
        double speed = clip.Speed;
        double playRate = speed <= 0 ? 1 : speed;
        double sourceDuration = Math.Min(clip.DurSec * playRate,
            Math.Min(clip.SourceOut, asset.DurationSec) - clip.SourceIn);
        if (!double.IsFinite(playRate) || playRate <= 0 || sourceDuration <= 0)
        {
            Fail("The clip has no audio range to analyze.");
            return;
        }

        var session = new AnalyzedClip(editor, clip, clip.MediaId!, clip.SourceIn, clip.SourceOut, clip.DurSec,
            speed, playRate);
        Discard();
        int generation = ++_generation;
        IsAnalyzing = true;
        Status = "Analyzing…";
        try
        {
            var analyzer = _host.CreateBeatAnalyzer();
            var analysis = await Task.Run(() => analyzer.Analyze(asset.Url, session.SrcIn, sourceDuration));

            // Cancelled, superseded, or the clip changed while decoding: this result is not for the document.
            if (generation != _generation)
                return;
            if (!IsCurrent(session))
            {
                Fail("The clip changed during analysis. Analyze again.");
                return;
            }
            _session = session;
            _analysis = analysis;
            HasPreview = true;
            Status = "";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                Fail($"Beat analysis failed: {CrashLog.Write("beat-analysis", ex)}");
        }
        finally
        {
            if (generation == _generation)
                IsAnalyzing = false;
        }
    }

    /// <summary>Commits the previewed markers, replacing the clip's earlier generated markers, as one undo step.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (_session is not { } session || !IsCurrent(session))
        {
            Cancel();
            return;
        }
        string kind = KicksOnly ? "kick" : "beat";
        // End the session first: committing raises TimelineChanged, which must not read as "clip changed".
        Discard(clearPreview: false);
        var markers = session.Editor.CommitBeatPreview();
        _host.Properties.Refresh();
        _host.Notify?.Invoke(markers.Count == 0 ? "No beat markers added" : $"Added {markers.Count} {kind} markers");
    }

    /// <summary>Drops the candidate markers and any analysis in flight. Nothing in the document changes.</summary>
    [RelayCommand]
    public void Cancel()
    {
        Discard();
        Status = "";
    }

    /// <summary>Called when the timeline changes; ends the session if the analyzed clip no longer matches.</summary>
    internal void OnTimelineChanged()
    {
        if (_session is { } session && !IsCurrent(session))
        {
            Discard();
            Fail("The clip changed, so the beat preview was cleared.");
        }
    }

    // The flyout may be closed (the analysis can start from the menu), so also surface it as a toast.
    private void Fail(string message)
    {
        Status = message;
        _host.Notify?.Invoke(message);
    }

    private void Discard(bool clearPreview = true)
    {
        _generation++; // invalidates any analysis still running
        if (clearPreview)
            _session?.Editor.ClearBeatPreview();
        _session = null;
        _analysis = null;
        IsAnalyzing = false;
        HasPreview = false;
        MarkerCount = 0;
    }

    private bool IsCurrent(AnalyzedClip s)
        => ReferenceEquals(_host.Editor, s.Editor)
            && s.Editor.Document.Tracks.SelectMany(track => track.Clips).Contains(s.Clip)
            && s.Clip.MediaId == s.SourceId && s.Clip is AudioClip or VideoClip { PlaysAudio: true }
            && s.Clip.SourceIn == s.SrcIn && s.Clip.SourceOut == s.SrcOut
            && s.Clip.DurSec == s.Dur && s.Clip.Speed == s.Speed;

    /// <summary>Re-selects markers from the cached analysis; cheap enough to run on every slider tick.</summary>
    private void RefreshPreview()
    {
        if (_session is not { } session || _analysis is not { } analysis)
            return;
        var local = analysis.Select(Sensitivity / 100, KicksOnly)
            .Select(time => (time - session.SrcIn) / session.PlayRate)
            .Where(time => time >= 0 && time < session.Dur)
            .ToArray();
        session.Editor.SetBeatPreview(session.Clip, local);
        MarkerCount = session.Editor.BeatPreview?.LocalTimes.Count ?? 0;
        Status = MarkerCount == 0 ? "No hits at this sensitivity. Move the slider toward More hits." : "";
    }
}
