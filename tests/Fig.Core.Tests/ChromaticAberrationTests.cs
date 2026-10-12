using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class ChromaticAberrationTests
{
    private const int W = 40, H = 10;

    /// <summary>Black frame with one white pixel at (20, 5).</summary>
    private static DecodedFrame WhiteDot()
    {
        var px = new byte[W * H * 4];
        for (var i = 3; i < px.Length; i += 4)
            px[i] = 255;
        var d = (5 * W + 20) * 4;
        px[d] = px[d + 1] = px[d + 2] = 255;
        return new DecodedFrame { Width = W, Height = H, Pixels = px };
    }

    private static DecodedFrame Run(DecodedFrame frame, double x, double y, bool radial = false)
    {
        var p = new Dictionary<string, ParamValue>
        {
            ["radial"] = ParamValue.OfBool(radial),
            ["x_offset"] = ParamValue.OfDouble(x),
            ["y_offset"] = ParamValue.OfDouble(y),
        };
        return EffectCatalog.Resolve(EffectCatalog.ChromaticAberration)!.Apply(frame, p, 0);
    }

    private static (byte B, byte G, byte R) At(DecodedFrame f, int x, int y)
    {
        var i = (y * f.Width + x) * 4;
        return (f.Pixels[i], f.Pixels[i + 1], f.Pixels[i + 2]);
    }

    [Fact]
    public void ZeroOffsets_ReturnTheFrameUntouched()
    {
        var frame = WhiteDot();
        Assert.Same(frame, Run(frame, 0, 0));
    }

    [Fact]
    public void RedAndBlue_MoveOppositeWays_GreenStaysPut()
    {
        // 0.05 of a 40 px frame is a 2 px shift
        var result = Run(WhiteDot(), 0.05, 0);

        Assert.Equal((byte)0, At(result, 20, 5).B);          // blue left the dot's position...
        Assert.Equal((byte)255, At(result, 20, 5).G);        // ...green did not
        Assert.Equal((byte)255, At(result, 18, 5).B);        // blue is sampled from +x, so it lands at x - 2
        Assert.Equal((byte)255, At(result, 22, 5).R);        // red is sampled from -x, so it lands at x + 2
        Assert.Equal((byte)0, At(result, 18, 5).R);
    }

    [Fact]
    public void AlphaIsCopiedUnchanged()
    {
        var frame = WhiteDot();
        frame.Pixels[3] = 0;   // a letterbox-style transparent pixel
        var result = Run(frame, 0.05, 0.05);
        Assert.Equal((byte)0, result.Pixels[3]);
        Assert.Equal((byte)255, result.Pixels[7]);
    }

    [Fact]
    public void Radial_LeavesTheCentreAlone_AndFringesTheEdges()
    {
        // a bright vertical bar at x = 2, near the left edge
        var px = new byte[W * H * 4];
        for (var i = 3; i < px.Length; i += 4)
            px[i] = 255;
        for (var y = 0; y < H; y++)
        {
            var d = (y * W + 2) * 4;
            px[d] = px[d + 1] = px[d + 2] = 255;
        }
        var result = Run(new DecodedFrame { Width = W, Height = H, Pixels = px }, 0.05, 0.05, radial: true);

        var (b, g, r) = At(result, 2, 5);
        Assert.Equal((byte)255, g);
        Assert.True(r < 255 || b < 255, "channels near the edge should separate");
    }
}
