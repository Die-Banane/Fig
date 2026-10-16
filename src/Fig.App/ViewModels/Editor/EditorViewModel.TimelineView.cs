using CommunityToolkit.Mvvm.Input;

namespace Fig.App.ViewModels;

/// <summary>How the timeline's visible range changes. The menu asks; the timeline view does it.</summary>
public enum TimelineZoom
{
    In,
    Out,
    FitSequence,
}

public partial class EditorViewModel
{
    /// <summary>Raised when something outside the timeline (the View menu) wants its zoom changed.</summary>
    public event Action<TimelineZoom>? TimelineZoomRequested;

    [RelayCommand]
    private void ZoomTimelineIn() => TimelineZoomRequested?.Invoke(TimelineZoom.In);

    [RelayCommand]
    private void ZoomTimelineOut() => TimelineZoomRequested?.Invoke(TimelineZoom.Out);

    [RelayCommand]
    private void ZoomTimelineToFit() => TimelineZoomRequested?.Invoke(TimelineZoom.FitSequence);
}
