using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class SpeedCurveTests
{
    private static KeyframePoint Key(double t, double speed, KeyframeEasing easing = KeyframeEasing.Linear)
        => new(t, ParamValue.OfDouble(speed), easing);

    private static VideoClip Ramped(KeyframeEasing easing)
    {
        var clip = TimelineFixtures.Video("v", 0, 6, 10);
        clip.SrcOutSec = 100;
        clip.Keyframes[AutomationKeys.Speed] = [Key(1, 0.5, easing), Key(3, 4, easing), Key(5, 1)];
        return clip;
    }

    [Theory]
    [InlineData(KeyframeEasing.Linear)]
    [InlineData(KeyframeEasing.EaseIn)]
    [InlineData(KeyframeEasing.EaseOut)]
    [InlineData(KeyframeEasing.EaseInOut)]
    [InlineData(KeyframeEasing.Hold)]
    public void SourceAt_IsTheAreaUnderTheSpeedCurve_AndLocalAtInvertsIt(KeyframeEasing easing)
    {
        var clip = Ramped(easing);
        double area = 0, step = 1e-4;
        for (double t = 0; t < 6 - step / 2; t += step)
        {
            area += ClipTime.SpeedAt(clip, t + step / 2) * step;
            if (Math.Abs(t + step - Math.Round(t + step)) < step / 2)
            {
                var whole = Math.Round(t + step);
                Assert.Equal(10 + area, ClipTime.SourceAt(clip, whole), 3);
                Assert.Equal(whole, ClipTime.LocalAt(clip, ClipTime.SourceAt(clip, whole)), 4);
            }
        }
    }

    [Fact]
    public void ACutThroughARamp_PlaysBothHalvesAsTheWholeDid()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = Ramped(KeyframeEasing.Linear);
        editor.AddClip(track.Id, clip);
        var expected = new[] { 0.5, 1.7, 2.4, 3.9, 5.5 }.Select(t => ClipTime.SourceAt(clip, t)).ToArray();

        editor.Cut(clip.Id, 2.4);

        var (left, right) = (track.Clips[0], track.Clips[1]);
        Assert.Equal(left.SourceOut, right.SourceIn, 9);
        var actual = new[] { 0.5, 1.7, 2.4, 3.9, 5.5 }
            .Select(t => t < 2.4 ? ClipTime.SourceAt(left, t) : ClipTime.SourceAt(right, t - 2.4)).ToArray();
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], 6);

        editor.Undo();
        Assert.Single(track.Clips);
        Assert.Equal(3, ClipTime.Curve(track.Clips[0])!.Count);
        Assert.Equal(expected[3], ClipTime.SourceAt(track.Clips[0], 3.9), 6);
    }

    [Fact]
    public void ShapingTheCurve_KeepsTheClipsLength_AndUndoesAsOneStep()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("v", 0, 4, 0);
        clip.SrcOutSec = 4;
        editor.AddClip(track.Id, clip);

        editor.AddSpeedCurve(clip.Id);
        Assert.Equal(4, clip.SourceOut, 9);                         // a flat curve at 1x changes nothing
        for (var speed = 1.2; speed <= 2.01; speed += 0.2)          // one drag
            editor.SetSpeedCurve(clip.Id, [Key(0, 1), Key(4, speed)], "Move speed point", "drag-1");

        Assert.Equal(4, clip.DurSec);
        Assert.Equal(6, clip.SourceOut, 6);                         // ramping 1x to 2x uses 6 s of footage
        editor.SetSpeed(clip.Id, 3);                                // the curve wins over the slider
        Assert.Equal(4, clip.DurSec);

        editor.Undo();
        Assert.Equal(4, clip.SourceOut, 9);
        Assert.Equal(2, ClipTime.Curve(clip)!.Count);
        editor.Undo();
        Assert.False(ClipTime.HasCurve(clip));
    }
}
