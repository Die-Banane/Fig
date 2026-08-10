using Fig.Core.Media;

namespace Fig.Core.Tests;

public class FrameCompositorTests
{
    private static DecodedFrame MakeFrame(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b;        // BGRA: [0]=B
            px[i + 1] = g;    //        [1]=G
            px[i + 2] = r;    //        [2]=R
            px[i + 3] = 255;
        }
        return new DecodedFrame { Width = w, Height = h, Pixels = px };
    }

    [Fact]
    public void Compose_EmptyLayers_ReturnsOpaqueBlack()
    {
        var result = FrameCompositor.Compose(Array.Empty<CompositeLayer>(), 2, 2);

        Assert.Equal(2 * 2 * 4, result.Pixels.Length);
        Assert.All(Enumerable.Range(0, result.Pixels.Length / 4), i =>
        {
            var p = i * 4;
            Assert.Equal(0, result.Pixels[p]);
            Assert.Equal(0, result.Pixels[p + 1]);
            Assert.Equal(0, result.Pixels[p + 2]);
            Assert.Equal(255, result.Pixels[p + 3]);
        });
    }

    [Fact]
    public void Compose_SingleOpaqueLayer_DrawsItsPixels()
    {
        var red = MakeFrame(2, 2, 255, 0, 0);

        var result = FrameCompositor.Compose(new[] { new CompositeLayer { Frame = red, Opacity = 1 } }, 2, 2);

        Assert.Equal(255, result.Pixels[2]);   // R = 255 (BGRA)
        Assert.Equal(0, result.Pixels[1]);     // G = 0
        Assert.Equal(0, result.Pixels[0]);     // B = 0
    }

    [Fact]
    public void Compose_TopLayerCoversBottom_WhenFullyOpaque()
    {
        var red = MakeFrame(2, 2, 255, 0, 0);
        var blue = MakeFrame(2, 2, 0, 0, 255);

        // topmost = red (first), covers blue below
        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = red, Opacity = 1 },
            new CompositeLayer { Frame = blue, Opacity = 1 },
        }, 2, 2);

        Assert.Equal(255, result.Pixels[2]);   // red's R = 255
        Assert.Equal(0, result.Pixels[0]);     // red's B = 0
    }

    [Fact]
    public void Compose_Reorder_ChangesResult_TopmostWins()
    {
        var red = MakeFrame(1, 1, 255, 0, 0);
        var blue = MakeFrame(1, 1, 0, 0, 255);

        var topRed = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = red, Opacity = 1 },
            new CompositeLayer { Frame = blue, Opacity = 1 },
        }, 1, 1);
        Assert.Equal(255, topRed.Pixels[2]);   // red's R on top

        var topBlue = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = blue, Opacity = 1 },
            new CompositeLayer { Frame = red, Opacity = 1 },
        }, 1, 1);
        Assert.Equal(255, topBlue.Pixels[0]);  // blue's B on top
    }

    [Fact]
    public void Compose_HalfOpacity_BlendsTopWithBottom()
    {
        var red = MakeFrame(1, 1, 255, 0, 0);
        var blue = MakeFrame(1, 1, 0, 0, 255);

        // red at 50% over blue: R = (0*128 + 255*128)/255 = 128, B = (255*128 + 0*128)/255 = 128
        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = red, Opacity = 0.5 },
            new CompositeLayer { Frame = blue, Opacity = 1 },
        }, 1, 1);

        Assert.InRange(result.Pixels[2], 125, 131);   // R ~128
        Assert.InRange(result.Pixels[0], 125, 131);   // B ~128
    }

    [Fact]
    public void Compose_ZeroOpacityLayer_IsInvisible()
    {
        var red = MakeFrame(1, 1, 255, 0, 0);
        var blue = MakeFrame(1, 1, 0, 0, 255);

        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = red, Opacity = 0 },
            new CompositeLayer { Frame = blue, Opacity = 1 },
        }, 1, 1);

        Assert.Equal(255, result.Pixels[0]);   // blue's B = 255 shows through
    }

    [Fact]
    public void Compose_NullFrameLayer_Skipped()
    {
        var blue = MakeFrame(1, 1, 0, 0, 255);

        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = null, Opacity = 1 },
            new CompositeLayer { Frame = blue, Opacity = 1 },
        }, 1, 1);

        Assert.Equal(255, result.Pixels[0]);
    }

    [Fact]
    public void Compose_Crop_ScalesInnerRegionToCanvas()
    {
        // 4x4: left half red, right half blue (visual coords, bottom-up storage)
        var w = 4;
        var h = 4;
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var visualY = h - 1 - y;
            var i = (visualY * w + x) * 4;
            if (x < 2)
            {
                px[i] = 0; px[i + 1] = 0; px[i + 2] = 255; px[i + 3] = 255; // red
            }
            else
            {
                px[i] = 255; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = 255; // blue
            }
        }
        var frame = new DecodedFrame { Width = w, Height = h, Pixels = px };

        // crop to right half only → entire canvas should be blue
        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer
            {
                Frame = frame,
                Opacity = 1,
                Crop = new RectI(2, 0, 2, 4),
            },
        }, w, h);

        Assert.Equal(255, result.Pixels[0]); // B
        Assert.Equal(0, result.Pixels[2]);   // R
    }

    /// <summary>
    /// Exercises the AVX2 BlendAlpha loop (frames wide enough to take the SIMD path, unlike the
    /// 1x1 frames above) and asserts byte-exact equality with the scalar integer math. Guards
    /// against the permute/lane bugs that previously scrambled the middle bytes of every
    /// 32-byte chunk and the (sum*257)&gt;&gt;16 divide off-by-one.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(254)]
    public void BlendAlpha_Simd_MatchesScalar_ByteExact(byte alpha)
    {
        var w = 16;
        var h = 16;
        var rng = new Random(alpha * 131 + 7);
        var src = new byte[w * h * 4];
        for (var i = 0; i < src.Length; i++)
            src[i] = (byte)rng.Next(256);

        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = new DecodedFrame { Width = w, Height = h, Pixels = src }, Opacity = alpha / 255.0 },
        }, w, h);

        // exact reference: opaque black canvas, then (dst*inv + src*alpha)/255 per channel
        var inv = 255 - alpha;
        for (var i = 0; i < result.Pixels.Length; i++)
        {
            var canvas = i % 4 == 3 ? 255 : 0;
            var expected = (byte)((canvas * inv + src[i] * alpha) / 255);
            Assert.Equal(expected, result.Pixels[i]);
        }
    }

    [Fact]
    public void BlendAlpha_ExactMultiples_NotOffByOne()
    {
        // dst=255, src=255, alpha=128 → sum = 255*127 + 255*128 = 65025 → /255 = 255.
        // (sum*257)>>16 gives 254; the exact carry-based divide must give 255.
        var w = 8;
        var h = 1;
        var white = new byte[w * h * 4];
        for (var i = 0; i < white.Length; i++)
            white[i] = 255;
        var bottom = new CompositeLayer { Frame = new DecodedFrame { Width = w, Height = h, Pixels = white }, Opacity = 1 };
        var top = new CompositeLayer { Frame = new DecodedFrame { Width = w, Height = h, Pixels = white }, Opacity = 128 / 255.0 };
        var result = FrameCompositor.Compose(new[] { top, bottom }, w, h);

        Assert.All(result.Pixels, p => Assert.Equal(255, p));
    }

    [Fact]
    public void BlendAlpha_Sum255_NotOffByOne()
    {
        // dst=0, src=255, alpha=1 → sum = 255 → /255 = 1 (SIMD (255*257)>>16 gives 0).
        var w = 8;
        var h = 1;
        var src = new byte[w * h * 4];
        for (var i = 0; i < src.Length; i++)
            src[i] = 255;
        var result = FrameCompositor.Compose(new[]
        {
            new CompositeLayer { Frame = new DecodedFrame { Width = w, Height = h, Pixels = src }, Opacity = 1 / 255.0 },
        }, w, h);

        for (var i = 0; i < result.Pixels.Length; i++)
        {
            var expected = i % 4 == 3 ? (byte)255 : (byte)1;
            Assert.Equal(expected, result.Pixels[i]);
        }
    }
}
