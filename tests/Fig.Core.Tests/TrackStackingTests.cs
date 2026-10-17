using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class TrackStackingTests
{
    [Fact]
    public void AHigherTrackCoversTheOnesBelow_AndMovingATrackChangesWhichIsOnTop()
    {
        var (editor, lower) = TimelineFixtures.Create();
        var upper = editor.AddTrack(TrackKind.Video);            // a new video track goes on top
        Assert.Same(upper, editor.Document.Tracks[0]);
        var top = new TextClip { Id = "top", StartSec = 0, DurSec = 5 };
        var bottom = new TextClip { Id = "bottom", StartSec = 0, DurSec = 5 };
        editor.AddClip(upper.Id, top);
        editor.AddClip(lower.Id, bottom);
        Clip? Picked() => editor.LayerAt(1, 0.5, 0.5, 16 / 9.0, _ => new LayerSize(1, 1));

        Assert.Equal("top", Picked()?.Id);
        Assert.Equal([upper, lower], TrackStacking.TopDown(editor.Document).Select(t => t.Track));

        Assert.True(editor.MoveTrack(upper.Id, 2));               // drag it to the bottom
        Assert.Equal([lower, upper], editor.Document.Tracks);
        Assert.Equal("bottom", Picked()?.Id);

        editor.Undo();
        Assert.Equal([upper, lower], editor.Document.Tracks);
    }
}

public class CrossTrackMoveTests
{
    [Fact]
    public void AClip_MovesOntoAnotherTrack_WhereThePointerPutsIt_AndUndoesBackToItsOwnTrack()
    {
        var (editor, a) = TimelineFixtures.Create();
        var b = editor.AddTrack(TrackKind.Video);
        var yuto = TimelineFixtures.Video("yuto", 0, 5);
        var carti = TimelineFixtures.Video("carti", 2, 2);    // under yuto's time on the other track
        editor.AddClip(a.Id, yuto);
        editor.AddClip(b.Id, carti);
        var before = new[] { editor.CaptureGeometry(carti) };

        // straight up it would land on yuto: refused, nothing moves
        Assert.False(editor.MoveClipsToTrack([carti.Id], a.Id, new Dictionary<string, double> { [carti.Id] = 1 }));
        Assert.Contains(carti, b.Clips);
        Assert.Equal(2, carti.StartSec);

        // dragged past yuto's end, it goes there
        Assert.True(editor.MoveClipsToTrack([carti.Id], a.Id, new Dictionary<string, double> { [carti.Id] = 6 }));
        Assert.Contains(carti, a.Clips);
        Assert.Equal(6, carti.StartSec);

        editor.CommitClipGeometry(before);   // the drag ends: one undo step
        editor.Undo();
        Assert.Contains(carti, b.Clips);
        Assert.DoesNotContain(carti, a.Clips);
        Assert.Equal(2, carti.StartSec);
        editor.Redo();
        Assert.Contains(carti, a.Clips);
    }
}
