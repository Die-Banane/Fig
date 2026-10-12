using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class MarkerRangeTests
{
    private static (TimelineEditor Editor, Track Track) Sample()
    {
        var (editor, track) = TimelineFixtures.Create();
        editor.AddClip(track.Id, TimelineFixtures.Video("a", 0, 12));
        return (editor, track);
    }

    [Fact]
    public void AddRangeMarker_IsRange_AndUndoes()
    {
        var (editor, _) = Sample();
        var marker = editor.AddRangeMarker(4, 8, "Save changes", "#5ba3b0");

        Assert.True(marker.IsRange);
        Assert.Equal(12, marker.EndSec, 3);
        Assert.Same(marker, Assert.Single(editor.Document.Markers));

        editor.Undo();
        Assert.Empty(editor.Document.Markers);
        editor.Redo();
        Assert.Single(editor.Document.Markers);
    }

    [Fact]
    public void EditMarker_DragCoalescesIntoOneUndoStep()
    {
        var (editor, _) = Sample();
        var marker = editor.AddRangeMarker(0, 4, "Intro");

        editor.EditMarker(marker.Id, new MarkerEdit(DurSec: 5));
        editor.EditMarker(marker.Id, new MarkerEdit(DurSec: 6));
        editor.EditMarker(marker.Id, new MarkerEdit(DurSec: 7));
        Assert.Equal(7, marker.DurSec, 3);

        editor.Undo();
        Assert.Equal(4, marker.DurSec, 3);
    }

    [Fact]
    public void EditMarker_CanRenameAndSetExportOptions()
    {
        var (editor, _) = Sample();
        var marker = editor.AddRangeMarker(0, 4, "Intro");

        editor.EditMarker(marker.Id, new MarkerEdit(Name: "Opening", IncludeInExport: false, OutputName: "01-opening.mp4"));
        Assert.Equal("Opening", marker.Name);
        Assert.False(marker.IncludeInExport);
        Assert.Equal("01-opening.mp4", marker.OutputName);

        editor.Undo();
        Assert.Equal("Intro", marker.Name);
        Assert.True(marker.IncludeInExport);
        Assert.Null(marker.OutputName);
    }

    [Fact]
    public void BookmarkRanges_WithSequenceBounds_SplitsAtEachBookmark()
    {
        var (editor, _) = Sample();
        editor.AddMarker(editor.Document, 5, "Zoom finishes");

        var plan = BookmarkRanges.Plan(editor.Document, includeSequenceBounds: true);

        Assert.Equal(2, plan.Count);
        Assert.Equal("Sequence start \u2192 Zoom finishes", plan[0].Name);
        Assert.Equal((0, 5), (plan[0].StartSec, plan[0].EndSec));
        Assert.Equal("Zoom finishes \u2192 Sequence end", plan[1].Name);
        Assert.Equal((5, 12), (plan[1].StartSec, plan[1].EndSec));
    }

    [Fact]
    public void BookmarkRanges_WithoutBounds_NeedsTwoBookmarks_AndIgnoresRanges()
    {
        var (editor, _) = Sample();
        editor.AddRangeMarker(0, 4, "Intro");
        editor.AddMarker(editor.Document, 5, "A");
        Assert.Empty(BookmarkRanges.Plan(editor.Document, includeSequenceBounds: false));

        editor.AddMarker(editor.Document, 9, "B");
        var plan = Assert.Single(BookmarkRanges.Plan(editor.Document, includeSequenceBounds: false));
        Assert.Equal((5, 9), (plan.StartSec, plan.EndSec));
    }

    [Fact]
    public void CreateRangesFromBookmarks_KeepsBookmarks_AndUndoesAsOne()
    {
        var (editor, _) = Sample();
        editor.AddMarker(editor.Document, 5, "Zoom finishes");

        var created = editor.CreateRangesFromBookmarks(includeSequenceBounds: true);

        Assert.Equal(2, created.Count);
        Assert.Equal(3, editor.Document.Markers.Count);
        Assert.Single(editor.Document.Markers, m => !m.IsRange);

        editor.Undo();
        Assert.Single(editor.Document.Markers);
    }

    [Fact]
    public void AddRangeFromBookmark_AsStart_RunsToNextBookmarkOrEnd()
    {
        var (editor, _) = Sample();
        var a = editor.AddMarker(editor.Document, 3, "A");
        editor.AddMarker(editor.Document, 8, "B");

        var first = editor.AddRangeFromBookmark(a.Id, asStart: true)!;
        Assert.Equal((3, 8), (first.StartSec, first.EndSec));

        var last = editor.Document.Markers.First(m => m.Name == "B");
        var tail = editor.AddRangeFromBookmark(last.Id, asStart: true)!;
        Assert.Equal((8, 12), (tail.StartSec, tail.EndSec));
    }

    [Fact]
    public void AddRangeFromBookmark_AsEnd_StartsAtPreviousBookmarkOrZero()
    {
        var (editor, _) = Sample();
        var a = editor.AddMarker(editor.Document, 3, "A");
        var b = editor.AddMarker(editor.Document, 8, "B");

        var head = editor.AddRangeFromBookmark(a.Id, asStart: false)!;
        Assert.Equal((0, 3), (head.StartSec, head.EndSec));
        var mid = editor.AddRangeFromBookmark(b.Id, asStart: false)!;
        Assert.Equal((3, 8), (mid.StartSec, mid.EndSec));
        Assert.Null(editor.AddRangeFromBookmark(head.Id, asStart: true));
    }
}

public class MarkerSnapTests
{
    [Fact]
    public void MagneticSnap_SnapsToMarkersAndRangeEdges()
    {
        var (editor, track) = TimelineFixtures.Create();
        editor.AddClip(track.Id, TimelineFixtures.Video("a", 0, 12));
        editor.MagneticSnap = true;
        editor.AddMarker(editor.Document, 5, "Beat");
        editor.AddRangeMarker(8, 2, "Range");

        Assert.Equal(5, editor.SnapTimeMagnetic(5.1), 3);
        Assert.Equal(8, editor.SnapTimeMagnetic(7.9), 3);
        Assert.Equal(10, editor.SnapTimeMagnetic(10.15), 3);
    }

    [Fact]
    public void MagneticSnap_IgnoresTheMarkerBeingDragged()
    {
        var (editor, track) = TimelineFixtures.Create();
        editor.AddClip(track.Id, TimelineFixtures.Video("a", 0, 12));
        editor.MagneticSnap = true;
        var marker = editor.AddMarker(editor.Document, 5, "Beat");

        // without the exclusion a marker would snap onto itself and never move
        Assert.NotEqual(5, editor.SnapTimeMagnetic(5.1, null, marker.Id), 3);
    }

    [Fact]
    public void SnapOff_UsesTheFrameGridOnly()
    {
        var (editor, track) = TimelineFixtures.Create();
        editor.AddClip(track.Id, TimelineFixtures.Video("a", 0, 12));
        editor.MagneticSnap = false;
        editor.AddMarker(editor.Document, 5, "Beat");

        Assert.NotEqual(5, editor.SnapTimeMagnetic(5.1), 3);
    }
}
