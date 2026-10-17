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
/// Markers and bookmarks: adding, picking, editing, ranges from bookmarks, cuts at markers.
/// </summary>
public partial class EditorViewModel
{
    /// <summary>
    /// Adds a marker at the playhead. Attaches to the selected clip (local offset), the
    /// active track, or the timeline, in that order of context. Selects the new marker.
    /// </summary>
    [RelayCommand]
    private void AddMarkerAtPlayhead()
    {
        var time = Editor.SnapTime(PlayheadTimeSec);
        Marker? marker;
        if (Editor.Selection.SelectedClipIds.FirstOrDefault() is { } clipId)
        {
            var clip = Editor.Document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            marker = clip is not null ? Editor.AddMarker(clip, time - clip.StartSec) : null;
        }
        else if (Editor.Selection.ActiveTrackId is { } trackId)
        {
            var track = Editor.Document.Tracks.FirstOrDefault(t => t.Id == trackId);
            marker = track is not null ? Editor.AddMarker(track, time) : null;
        }
        else
        {
            marker = Editor.AddMarker(Editor.Document, time);
        }

        if (marker is null)
            return;
        Editor.Selection.SelectMarker(marker.Id);
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke(marker.Name.Length == 0 ? "Added marker at playhead" : $"Added marker \"{marker.Name}\"");
    }

    /// <summary>
    /// Asks how to build ranges from the timeline's bookmarks (shows a preview), then adds them.
    /// Returns true when ranges were created.
    /// </summary>
    public async Task<bool> CreateRangesFromBookmarksAsync(Window? owner)
    {
        owner ??= MainWindow;
        if (owner is null)
            return false;
        var dialog = new BookmarkRangesDialog(Editor.Document);
        var includeBounds = await dialog.ShowDialog<bool?>(owner);
        if (includeBounds is null)
            return false;
        var created = Editor.CreateRangesFromBookmarks(includeBounds.Value);
        if (created.Count == 0)
            return false;
        Notify?.Invoke(created.Count == 1 ? "Created 1 range" : $"Created {created.Count} ranges");
        return true;
    }

    [RelayCommand]
    private async Task CreateRangesFromBookmarks(Window? owner) => await CreateRangesFromBookmarksAsync(owner);

    /// <summary>Selects a marker (from the Markers panel) and moves the playhead to it.</summary>
    public void SelectMarker(string markerId)
    {
        if (Editor.FindMarker(markerId) is not { } loc)
            return;
        Editor.Selection.SelectMarker(markerId);
        SeekFromUser((loc.Clip?.StartSec ?? 0) + loc.Marker.StartSec);
        Preview.RefreshFrame();
    }

    /// <summary>Deletes a marker by id.</summary>
    public void DeleteMarker(string markerId) => Editor.DeleteMarker(markerId);

    public void DeleteMarkerSelectionOrOne(string markerId)
    {
        if (Editor.Selection.IsMarkerSelected(markerId))
            DeleteSelectedMarker();
        else
            DeleteMarker(markerId);
    }

    /// <summary>Opens the inspector on the marker so it can be renamed, recolored or retimed.</summary>
    public void EditMarker(string markerId)
    {
        Editor.Selection.SelectMarker(markerId);
        Layout.RevealRight();
    }


    /// <summary>Analysis for the beat-marker session; the view model owns the analysis, not the host.</summary>
    internal BeatAnalysisService CreateBeatAnalyzer() => new(_mediaService);

    internal MediaAsset? FindAsset(string sourceId) => FindAssetById(sourceId);

    public void UseMarkerAsRangeStart(string markerId) => AddRangeFromBookmark(markerId, true);

    public void UseMarkerAsRangeEnd(string markerId) => AddRangeFromBookmark(markerId, false);

    private void AddRangeFromBookmark(string markerId, bool asStart)
    {
        var range = Editor.AddRangeFromBookmark(markerId, asStart);
        if (range is null)
        {
            Notify?.Invoke("There's no room for a range here — only bookmarks on the sequence can start or end one");
            return;
        }
        Editor.Selection.SelectMarker(range.Id);
        Notify?.Invoke($"Created range \"{range.Name}\"");
    }

    /// <summary>True when an effect is selected, so a keyframe can be placed at a marker.</summary>
    public bool CanPlaceKeyframeAtMarker => Properties.SelectedEffect is not null;

    /// <summary>Moves the playhead to the marker and keyframes the selected effect there.</summary>
    public void PlaceKeyframeAtMarker(string markerId)
    {
        Editor.Selection.SelectMarker(markerId);
        Properties.PlaceKeyframeAtMarkerCommand.Execute(null);
    }

    /// <summary>Adds a bookmark (point marker) to the sequence at the playhead.</summary>
    public void AddTimelinePointMarker()
    {
        var count = Editor.Document.Markers.Count(m => !m.IsRange) + 1;
        var marker = Editor.AddMarker(Editor.Document, Editor.SnapTime(PlayheadTimeSec), $"Marker {count}", "#eab308");
        Editor.Selection.SelectMarker(marker.Id);
        Notify?.Invoke($"Added \"{marker.Name}\" at the playhead");
    }

    /// <summary>Adds a range marker starting at the playhead (up to 4 seconds, or until the end of the sequence).</summary>
    public void AddTimelineRangeMarker()
    {
        var start = Editor.SnapTime(PlayheadTimeSec);
        var end = Math.Max(SequenceEndSec, start + 1);
        var length = Math.Min(4, end - start);
        var count = Editor.Document.Markers.Count(m => m.IsRange) + 1;
        var marker = Editor.AddRangeMarker(start, length, $"Range {count}", "#5f9cf8");
        Editor.Selection.SelectMarker(marker.Id);
        Notify?.Invoke($"Added \"{marker.Name}\" \u2014 drag its edges on the timeline to adjust");
    }

    [RelayCommand]
    private void SplitVideoAtSelectedMarkers()
    {
        if (Editor.Selection.SelectedMarkerIds.Count == 0)
        {
            Notify?.Invoke("Select one or more markers first");
            return;
        }
        int count = Editor.SplitVideoAtSelectedMarkers();
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke(count == 0 ? "No video clips cross the selected marker positions"
            : $"Made {count} video cut{(count == 1 ? "" : "s")} at selected markers");
    }

    [RelayCommand]
    private void DeleteSelectedMarker()
    {
        int count = Editor.DeleteMarkers(Editor.Selection.SelectedMarkerIds);
        if (count == 0)
            return;
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke($"Deleted {count} marker{(count == 1 ? "" : "s")}");
    }
}
