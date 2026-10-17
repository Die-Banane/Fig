using Fig.Core.Timeline;

namespace Fig.Core.Tests;

/// <summary>Every kind of keyframe (a clip's own, an effect's, a speed curve's) behaves the same.</summary>
public class UnifiedKeyframeTests
{
    public enum Kind { ClipOwn, TrackEffect, SpeedCurve }

    /// <summary>A host of each kind with keys at 1 s and 3 s, on a clip that starts at 2 s.</summary>
    private static (TimelineEditor Editor, KeyframeHostRef Host, string Key) Setup(Kind kind)
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("v", 2, 6);
        clip.SrcOutSec = 100;
        editor.AddClip(track.Id, clip);
        switch (kind)
        {
            case Kind.ClipOwn:
                editor.SetClipKeyframe(clip.Id, AutomationKeys.Opacity, 1, 0.2);
                editor.SetClipKeyframe(clip.Id, AutomationKeys.Opacity, 3, 0.9);
                return (editor, KeyframeHostRef.OfClip(clip.Id), AutomationKeys.Opacity);
            case Kind.TrackEffect:
                var fx = Fig.Core.Media.EffectCatalog.Find(Fig.Core.Media.EffectCatalog.Brightness)!.CreateInstance();
                fx.StartSec = 2;
                fx.DurSec = 6;
                editor.AddEffect(track.Id, fx);
                editor.SetKeyframe(track.Id, fx.Id, "amount", 1, ParamValue.OfDouble(0.2));
                editor.SetKeyframe(track.Id, fx.Id, "amount", 3, ParamValue.OfDouble(0.9));
                return (editor, KeyframeHostRef.OfEffect(track.Id, fx.Id), "amount");
            default:
                editor.SetClipKeyframe(clip.Id, AutomationKeys.Speed, 1, 0.5);
                editor.SetClipKeyframe(clip.Id, AutomationKeys.Speed, 3, 2);
                return (editor, KeyframeHostRef.OfSpeed(clip.Id), AutomationKeys.Speed);
        }
    }

    private static IEnumerable<double> Times(TimelineEditor editor, KeyframeHostRef host) => editor.FindKeyframeHost(host)!.Times;

    [Theory]
    [InlineData(Kind.ClipOwn)]
    [InlineData(Kind.TrackEffect)]
    [InlineData(Kind.SpeedCurve)]
    public void EveryKindOfKeyframe_MovesCopiesEasesAndDeletesTheSameWay(Kind kind)
    {
        var (editor, host, key) = Setup(kind);
        Assert.Equal([1.0, 3.0], Times(editor, host));

        // a drag: several steps, one undo
        foreach (var to in new[] { 1.5, 2.0, 2.5 })
            editor.MoveKeyframes(new KeyframeRef(host, to - 0.5), to, "drag");
        Assert.Equal([2.5, 3.0], Times(editor, host));
        editor.Undo();
        Assert.Equal([1.0, 3.0], Times(editor, host));

        editor.DuplicateKeyframes(new KeyframeRef(host, 1), 5);
        Assert.Equal([1.0, 3.0, 5.0], Times(editor, host));
        var copied = editor.FindKeyframeHost(host)!.Tracks.Single(t => t.Key == key).Value;
        Assert.Equal(copied[0].Value.AsNumber, copied[2].Value.AsNumber, 9);   // the same value

        editor.SetKeyframeEasing(new KeyframeRef(host, 3), KeyframeEasing.Hold);
        Assert.Equal(KeyframeEasing.Hold, editor.FindKeyframeHost(host)!.EasingAt(3));

        editor.RemoveKeyframes(new KeyframeRef(host, 5));
        Assert.Equal([1.0, 3.0], Times(editor, host));

        // the stopwatch adds the current look, and never twice at one time
        Assert.Equal(2.0, editor.RecordKeyframe(host, 2));
        Assert.Equal(2.0, editor.RecordKeyframe(host, 2));
        Assert.Equal([1.0, 2.0, 3.0], Times(editor, host));

        // moves stay inside the host (a clip's length; a track effect has no end)
        editor.MoveKeyframes(new KeyframeRef(host, 3), 999, "far");
        Assert.Equal(Math.Min(editor.FindKeyframeHost(host)!.LimitSec, 999), Times(editor, host).Max(), 9);
    }

    [Fact]
    public void ASpeedCurveEdit_KeepsTheClipsOutPointInStep_ThroughUndo()
    {
        var (editor, host, _) = Setup(Kind.SpeedCurve);
        var clip = editor.FindClipForView("v")!;
        var before = clip.SourceOut;
        Assert.Equal(ClipTime.SourceAt(clip, clip.DurSec), before, 9);

        editor.MoveKeyframes(new KeyframeRef(host, 3), 5, "drag");
        Assert.Equal(ClipTime.SourceAt(clip, clip.DurSec), clip.SourceOut, 9);
        Assert.NotEqual(before, clip.SourceOut, 6);

        editor.Undo();
        Assert.Equal(before, clip.SourceOut, 9);
    }
}
