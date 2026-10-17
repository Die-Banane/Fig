using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class EffectClipboardTests
{
    [Fact]
    public void Effects_CopiedFromAClip_PasteOntoOtherClips_AtTheSameOffset_CutToFit()
    {
        var (editor, track) = TimelineFixtures.Create();
        var a = TimelineFixtures.Video("a", 0, 4);
        var b = TimelineFixtures.Video("b", 10, 4);
        var c = TimelineFixtures.Video("c", 20, 1.5);
        editor.AddClip(track.Id, a);
        editor.AddClip(track.Id, b);
        editor.AddClip(track.Id, c);
        // a region 1 s into clip a, 2 s long, with a keyframe at its start
        var blur = new EffectInstance { TypeId = "blur", StartSec = 1, DurSec = 2 };
        blur.Keyframes["amount"] = [new KeyframePoint(1, ParamValue.OfDouble(0.5))];
        editor.AddEffect(track.Id, blur);

        Assert.Equal(1, editor.CopyEffects(a.Id));
        Assert.Equal(2, editor.PasteEffects([b.Id, c.Id]));

        var onB = track.Effects.Single(fx => fx.StartSec is > 9 and < 19);
        Assert.Equal((11, 2), (onB.StartSec!.Value, onB.DurSec!.Value));
        Assert.Equal(11, onB.Keyframes["amount"][0].TimeSec);
        var onC = track.Effects.Single(fx => fx.StartSec is > 19);
        Assert.Equal((21, 0.5), (onC.StartSec!.Value, onC.DurSec!.Value));   // cut to the end of the short clip

        editor.Undo();
        Assert.Single(track.Effects);
    }
}
