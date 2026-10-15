using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class MarkerSelectionTests
{
    [Fact]
    public void ToggleAndRange_PreserveAnchor_AndClearClipSelection()
    {
        var selection = new SelectionState();
        selection.SelectOnly("clip");
        selection.SelectMarker("b");
        selection.ToggleMarker("d");
        Assert.Empty(selection.SelectedClipIds);
        Assert.Equal(2, selection.SelectedMarkerIds.Count);
        selection.SelectMarkerRange("e", new[] { "a", "b", "c", "d", "e" });
        Assert.Equal(new[] { "b", "c", "d", "e" }, selection.SelectedMarkerIds);
        selection.SelectMarkerRange("c", new[] { "a", "b", "c", "d", "e" });
        Assert.Equal(new[] { "b", "c" }, selection.SelectedMarkerIds);
        selection.ToggleMarker("b");
        selection.ToggleMarker("c");
        Assert.Null(selection.SelectedMarkerId);
        Assert.Empty(selection.SelectedMarkerIds);
        selection.SelectMarkers(new[] { "a", "b" });
        selection.SelectOnly("clip");
        Assert.Empty(selection.SelectedMarkerIds);
    }

    [Fact]
    public void Range_UsesAbsoluteTime_AndCanBeRestrictedToVisibleMarkers()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("a", 10, 10);
        editor.AddClip(track.Id, clip);
        var first = editor.AddMarker(editor.Document, 8);
        var hidden = editor.AddMarker(track, 10);
        var last = editor.AddMarker(clip, 2);
        editor.SelectMarker(first.Id);
        editor.SelectMarker(last.Id, extend: true);
        Assert.Equal(3, editor.Selection.SelectedMarkerIds.Count);
        editor.SelectMarker(first.Id);
        editor.SelectMarker(last.Id, extend: true, visibleIds: new[] { first.Id, last.Id });
        Assert.Equal(new[] { first.Id, last.Id }, editor.Selection.SelectedMarkerIds);
        Assert.False(editor.Selection.IsMarkerSelected(hidden.Id));
    }

    [Fact]
    public void BulkDelete_IsOneUndoStepAcrossOwners_AndPreservesMarkerOrder()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("a", 0, 10);
        editor.AddClip(track.Id, clip);
        var first = editor.AddMarker(clip, 1);
        var middle = editor.AddMarker(clip, 2);
        var last = editor.AddMarker(clip, 3);
        var sequence = editor.AddMarker(editor.Document, 4);
        var trackMarker = editor.AddMarker(track, 5);
        editor.Selection.SelectMarkers(new[] { first.Id, last.Id, sequence.Id, trackMarker.Id });

        Assert.Equal(4, editor.DeleteMarkers(editor.Selection.SelectedMarkerIds));
        Assert.Same(middle, Assert.Single(clip.Markers));
        Assert.Empty(editor.Document.Markers);
        Assert.Empty(track.Markers);
        Assert.Empty(editor.Selection.SelectedMarkerIds);
        editor.Undo();
        Assert.Equal(new[] { first, middle, last }, clip.Markers);
        Assert.Same(sequence, Assert.Single(editor.Document.Markers));
        Assert.Same(trackMarker, Assert.Single(track.Markers));
        editor.Redo();
        Assert.Same(middle, Assert.Single(clip.Markers));
        Assert.Empty(editor.Document.Markers);
        Assert.Empty(track.Markers);
    }

    [Fact]
    public void ClearGenerated_PreservesManualMarkers_AndUndoes()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = new AudioClip { DurSec = 10 };
        editor.AddClip(track.Id, clip);
        var manual = editor.AddMarker(clip, 1);
        var generated = editor.AddBeatMarkers(clip, new[] { 2.0, 3.0 }).ToArray();
        Assert.Equal(2, editor.ClearClipMarkers(clip, generatedOnly: true));
        Assert.Same(manual, Assert.Single(clip.Markers));
        editor.Undo();
        Assert.Equal(new[] { manual }.Concat(generated), clip.Markers);
        Assert.Equal(3, editor.ClearClipMarkers(clip));
        Assert.Empty(clip.Markers);
        editor.Undo();
        Assert.Equal(3, clip.Markers.Count);
    }
}
