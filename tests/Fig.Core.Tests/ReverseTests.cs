using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class ReverseGeneratorTests
{
    [Fact]
    public void ReversedCopy_IsAsLong_AndStartsWhereTheSourceEnds()
    {
        var media = new MediaService();
        var output = Path.Combine(Path.GetTempPath(), $"fig_reversed_{Guid.NewGuid():N}.mp4");
        try
        {
            media.GenerateReversed(TestAssets.Timer, output);

            var source = media.Probe(TestAssets.Timer);
            var reversed = media.Probe(output);
            Assert.True(reversed.HasAudio);
            Assert.InRange(reversed.DurationSec, source.DurationSec - 0.15, source.DurationSec + 0.15);

            // the copy ends where the source starts (the timer's "3"), and starts somewhere else entirely
            // (DecodeFrameAt shows the keyframe at or before a time: the source has one only at its start,
            // the copy one every half second)
            var sourceStart = media.DecodeFrameAt(TestAssets.Timer, 0, 160, 90)!;
            var copyStart = media.DecodeFrameAt(output, 0, 160, 90)!;
            var copyEnd = media.DecodeFrameAt(output, reversed.DurationSec - 0.05, 160, 90)!;
            Assert.True(Difference(copyEnd, sourceStart) < Difference(copyStart, sourceStart) / 2,
                $"copy end vs source start {Difference(copyEnd, sourceStart):0.0}, copy start vs source start {Difference(copyStart, sourceStart):0.0}");

            // and its sound is the source's, backwards
            var sourcePeaks = media.ExtractPeaks(TestAssets.Timer, 64);
            var copyPeaks = media.ExtractPeaks(output, 64);
            Assert.True(Correlation(copyPeaks, sourcePeaks.Reverse().ToArray()) > Correlation(copyPeaks, sourcePeaks),
                "the copy's waveform should match the source's reversed better than forwards");
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>How different the middle of two pictures is (where the timer's digit is; the rest is black).</summary>
    private static double Difference(DecodedFrame a, DecodedFrame b)
    {
        long sum = 0, n = 0;
        for (var y = a.Height * 3 / 10; y < a.Height * 7 / 10; y++)
            for (var x = a.Width * 4 / 10; x < a.Width * 6 / 10; x++)
                for (var c = 0; c < 3; c++, n++)
                    sum += Math.Abs(a.Pixels[(y * a.Width + x) * 4 + c] - b.Pixels[(y * b.Width + x) * 4 + c]);
        return sum / (double)Math.Max(1, n);
    }

    private static double Correlation(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double ma = a.Take(n).Average(), mb = b.Take(n).Average(), num = 0, da = 0, db = 0;
        for (var i = 0; i < n; i++)
        {
            num += (a[i] - ma) * (b[i] - mb);
            da += (a[i] - ma) * (a[i] - ma);
            db += (b[i] - mb) * (b[i] - mb);
        }
        return num / Math.Sqrt(Math.Max(1e-12, da * db));
    }
}

public class ReverseEditTests
{
    [Fact]
    public void ReversingAVideo_PlaysItsCopyOverTheMirroredRange_AndBackAgain()
    {
        var (editor, track) = TimelineFixtures.Create();
        var original = new MediaAsset { Id = "src", DurationSec = 10 };
        var copy = new MediaAsset { Id = "rev", DurationSec = 10, ReverseOf = "src" };
        var assets = new[] { original, copy };
        editor.MediaResolver = id => assets.FirstOrDefault(a => a.Id == id);
        MediaAsset Counterpart(MediaAsset a) => a == original ? copy : original;

        var clip = TimelineFixtures.Video("v", 0, 3, srcIn: 2);   // seconds 2..5 of the file
        clip.SourceId = "src";
        editor.AddClip(track.Id, clip);

        Assert.True(editor.Reverse(["v"], Counterpart));
        Assert.Equal("rev", clip.SourceId);
        Assert.Equal((5.0, 8.0), (clip.SourceIn, clip.SourceOut));   // the same stretch, backwards: 10 - 5 .. 10 - 2

        Assert.True(editor.Reverse(["v"], Counterpart));
        Assert.Equal(("src", 2.0, 5.0), (clip.SourceId, clip.SourceIn, clip.SourceOut));

        editor.Undo();
        Assert.Equal(("rev", 5.0, 8.0), (clip.SourceId, clip.SourceIn, clip.SourceOut));
    }

    [Fact]
    public void ReversingATitle_RunsItsAnimationBackwards()
    {
        var (editor, _) = TimelineFixtures.Create();
        var text = editor.AddText(null, 0, "Title", durSec: 4);
        var key = ClipProperties.Opacity.Key;
        text.Keyframes[key] = [new(0, ParamValue.OfDouble(0), KeyframeEasing.EaseIn), new(1, ParamValue.OfDouble(1))];

        Assert.True(editor.Reverse([text.Id], _ => null));
        Assert.Equal([(3.0, 1.0), (4.0, 0.0)], text.Keyframes[key].Select(k => (k.TimeSec, k.Value.AsNumber)));
        Assert.Equal(KeyframeEasing.EaseOut, text.Keyframes[key][0].Easing);   // the fade in, played out

        editor.Undo();
        Assert.Equal([(0.0, 0.0), (1.0, 1.0)], text.Keyframes[key].Select(k => (k.TimeSec, k.Value.AsNumber)));
    }
}
