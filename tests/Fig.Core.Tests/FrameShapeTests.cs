using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class FrameShapeTests
{
    [Fact]
    public void PickingAShape_SetsTheExportSize_AndUndoGoesBackToTheFootage()
    {
        var (editor, _) = TimelineFixtures.Create();
        Assert.Equal((1920, 1080), FrameShapes.Size(editor.Document.Shape, 1920, 1080));

        editor.SetFrameShape(FrameShape.Tall);
        Assert.Equal((1080, 1920), FrameShapes.Size(editor.Document.Shape, 1920, 1080));

        editor.Undo();
        Assert.Equal(FrameShape.Auto, editor.Document.Shape);
    }
}
