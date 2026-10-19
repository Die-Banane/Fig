using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class KeyedLayoutTests
{
    [Fact]
    public void DraggingAnAnimatedLayer_KeysItAtThePlayhead_AndUndoTakesTheKeyBack()
    {
        var (editor, _) = TimelineFixtures.Create();
        var text = editor.AddText(null, 0, "Title");
        // animate across: 0.2 at the start, 0.8 at 4 s
        editor.SetClipKeyframe(text.Id, ClipProperties.LayoutX.Key, 0, 0.2);
        editor.SetClipKeyframe(text.Id, ClipProperties.LayoutX.Key, 4, 0.8);
        var before = ClipProperties.LayoutAt(text, 2).CenterX;
        Assert.InRange(before, 0.21, 0.79);   // between the two keys

        // a gizmo drag at 2 s: the animated value gets a key there, the still one moves the layout
        var moved = ClipProperties.LayoutAt(text, 2) with { CenterX = 0.3, CenterY = 0.7 };
        editor.SetLayouts(new Dictionary<string, LayerTransform> { [text.Id] = moved }, playheadSec: 2);
        Assert.Equal(0.3, ClipProperties.LayoutAt(text, 2).CenterX, 3);
        Assert.Equal(3, text.Keyframes[ClipProperties.LayoutX.Key].Count);
        Assert.Equal(0.7, text.Layout.CenterY, 3);

        editor.Undo();
        Assert.Equal(2, text.Keyframes[ClipProperties.LayoutX.Key].Count);
        Assert.Equal(before, ClipProperties.LayoutAt(text, 2).CenterX, 6);
        Assert.Equal(0.5, text.Layout.CenterY, 3);
    }
}
