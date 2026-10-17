using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class EffectClipboardTests
{
    [Fact]
    public void SelectedEffects_CopyAndPasteAtThePlayhead_KeepingTheirSpacing_OnTheirTrack()
    {
        var (editor, track) = TimelineFixtures.Create();
        var other = editor.AddTrack(TrackKind.Video);
        var blur = new EffectInstance { TypeId = "blur", StartSec = 1, DurSec = 2 };
        blur.Keyframes["amount"] = [new KeyframePoint(1.5, ParamValue.OfDouble(0.5))];
        var shake = new EffectInstance { TypeId = "shake", StartSec = 4, DurSec = 1 };
        editor.AddEffect(track.Id, blur);
        editor.AddEffect(track.Id, shake);
        editor.Selection.SelectEffects([(track.Id, blur.Id), (track.Id, shake.Id)]);

        Assert.Equal(2, editor.CopySelectedEffects());
        Assert.True(editor.ClipboardHoldsEffects);
        var pasted = editor.PasteEffects(10);

        Assert.Equal(4, track.Effects.Count);                                   // back on the track they came from
        Assert.Equal((10.0, 2.0), (pasted[0].StartSec!.Value, pasted[0].DurSec!.Value));
        Assert.Equal(10.5, pasted[0].Keyframes["amount"][0].TimeSec, 9);        // keyframes move with it
        Assert.Equal(13, pasted[1].StartSec!.Value, 9);                          // still 3 s after the first
        Assert.Equal(2, editor.Selection.SelectedEffects.Count);

        // from one track, a chosen track of the same kind takes them
        editor.PasteEffects(20, other.Id);
        Assert.Equal(2, other.Effects.Count);

        editor.Undo();
        editor.Undo();
        Assert.Equal(2, track.Effects.Count);
        Assert.Empty(other.Effects);
    }
}
