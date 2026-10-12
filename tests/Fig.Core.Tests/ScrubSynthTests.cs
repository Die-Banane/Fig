using Fig.Core.Audio;

namespace Fig.Core.Tests;

public class ScrubSynthTests
{
    /// <summary>A "timeline" whose sample value is its own time, so the output shows where it was read from.</summary>
    private static float[] TimeRamp(double start, double length)
    {
        var frames = (int)(length * ScrubSynth.SampleRate);
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
            data[i * 2] = data[i * 2 + 1] = (float)(start + i / (double)ScrubSynth.SampleRate);
        return data;
    }

    [Fact]
    public void Render_FollowsTheSweep_ForwardFasterAndBackwards()
    {
        var synth = new ScrubSynth(TimeRamp);

        // 20 ms of timeline squeezed into 10 ms of output: plays at 2x
        var fast = synth.Render(1.0, 1.02, 480, fadeIn: false, fadeOut: false);
        Assert.Equal(1.0, fast[0], 3);
        Assert.Equal(1.02, fast[(480 - 1) * 2], 2);

        // scrubbing backwards reads the same audio in reverse
        var back = synth.Render(1.02, 1.0, 480, fadeIn: false, fadeOut: false);
        Assert.Equal(1.02, back[0], 3);
        Assert.True(back[(480 - 1) * 2] < back[0]);

        // fades start and end from silence so stops don't click
        var faded = synth.Render(1.0, 1.01, 480, fadeIn: true, fadeOut: true);
        Assert.Equal(0, faded[0], 5);
        Assert.Equal(0, faded[(480 - 1) * 2], 5);
    }
}
