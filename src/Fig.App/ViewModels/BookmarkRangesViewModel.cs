using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>Preview row in the "Create ranges from bookmarks" dialog.</summary>
public sealed record PlannedRangeRow(string Name, string RangeText);

/// <summary>Chooses how timeline bookmarks are turned into ranges, and previews the result.</summary>
public sealed partial class BookmarkRangesViewModel : ViewModelBase
{
    private readonly Timeline _timeline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(Count))]
    [NotifyPropertyChangedFor(nameof(CreateText))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private bool _includeSequenceBounds = true;

    public BookmarkRangesViewModel(Timeline timeline) => _timeline = timeline;

    public IReadOnlyList<PlannedRangeRow> Preview => BookmarkRanges.Plan(_timeline, IncludeSequenceBounds)
        .Select(r => new PlannedRangeRow(r.Name, $"{ExportDialogViewModel.Clock(r.StartSec)} → {ExportDialogViewModel.Clock(r.EndSec)}"))
        .ToList();

    public int Count => Preview.Count;
    public bool CanCreate => Count > 0;
    public string CreateText => Count == 1 ? "Create 1 range" : $"Create {Count} ranges";

    public string? EmptyMessage => CanCreate ? null
        : _timeline.Markers.Any(m => !m.IsRange)
            ? "Add a second bookmark, or include the sequence start and end, to make a range."
            : "This sequence has no bookmarks yet. Press M to add one at the playhead.";
}
