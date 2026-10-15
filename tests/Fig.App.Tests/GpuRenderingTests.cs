using Fig.App.Rendering;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;
using Xunit;

namespace Fig.App.Tests;

/// <summary>
/// The GPU shaders are ports of the CPU kernels, which export still uses. Every effect and
/// transition is rendered both ways from the same pictures and must agree to within rounding.
/// (The shaders run on Skia's raster backend here; the SkSL is identical on the GPU.)
/// </summary>
public class GpuRenderingTests
{
    [Theory]
    [InlineData("invert")]
    [InlineData("lens-distortion")]
    public void TrackEffectsApplyAfterTransitionsAtAbsoluteTimelineTime(string typeId)
    {
        var outgoing = MakeFrame(1);
        var incoming = MakeFrame(2);
        var track = new Track { Kind = TrackKind.Video };
        var effect = EffectCatalog.Find(typeId)!.CreateInstance();
        effect.StartSec = 5;
        effect.DurSec = 2;
        track.Effects.Add(effect);
        var parameters = new Dictionary<string, ParamValue>();
        var blended = TransitionCatalog.Resolve(TransitionCatalog.CrossDissolve)!.Blend(outgoing, incoming, 0.37, parameters);
        var expected = EffectPipeline.ApplyStack(blended, track.Effects, 5.5);
        var layer = new GpuLayer
        {
            Source = new GpuSource { Image = ToImage(outgoing) },
            Incoming = new GpuSource { Image = ToImage(incoming) },
            TransitionTypeId = TransitionCatalog.CrossDissolve, Progress = 0.37, TransitionParams = parameters,
        };
        using var scene = new GpuScene
        {
            Width = W, Height = H,
            Layers = new[] { GpuSceneBuilder.WithTrackEffects(layer, track, 5.5, W, H) },
        };
        AssertClose(expected.Pixels, GpuSceneRenderer.RenderToBgra(scene), typeId, meanLimit: 2, badFraction: 0.03);
    }

    [Fact]
    public void TrackEffectsAreInactiveOutsideTheirTimelineRange()
    {
        var track = new Track { Kind = TrackKind.Video };
        var effect = EffectCatalog.Find("invert")!.CreateInstance();
        effect.StartSec = 5;
        effect.DurSec = 2;
        track.Effects.Add(effect);
        Assert.Empty(GpuSceneBuilder.EffectsAt(track.Effects, 4.9));
        Assert.Single(GpuSceneBuilder.EffectsAt(track.Effects, 5.5));
        Assert.Empty(GpuSceneBuilder.EffectsAt(track.Effects, 7.1));
    }

    private const int W = 96;
    private const int H = 54;

    private static DecodedFrame MakeFrame(int seed)
    {
        var px = new byte[W * H * 4];
        var rng = new Random(seed);
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                var checker = ((x / 6) + (y / 6)) % 2 == 0 ? 40 : 210;
                px[i] = (byte)Math.Clamp(checker * (seed % 2 == 0 ? 1.0 : 0.5) + rng.Next(0, 20), 0, 255);   // B
                px[i + 1] = (byte)(y * 255 / (H - 1));                                                       // G
                px[i + 2] = (byte)(x * 255 / (W - 1));                                                       // R
                px[i + 3] = 255;
            }
        }
        return new DecodedFrame { Width = W, Height = H, Pixels = px };
    }

    private static SKImage ToImage(DecodedFrame f)
        => SKImage.FromPixelCopy(new SKImageInfo(f.Width, f.Height, SKColorType.Bgra8888, SKAlphaType.Opaque),
            f.Pixels.AsSpan(0, f.Width * f.Height * 4));

    private static Dictionary<string, ParamValue> P(params (string, ParamValue)[] items)
        => items.ToDictionary(i => i.Item1, i => i.Item2);

    private static void AssertClose(byte[] cpu, byte[] gpu, string label, double meanLimit = 1.5, double badFraction = 0.01, int badThreshold = 10)
    {
        long sum = 0;
        var bad = 0;
        var n = W * H;
        var worst = 0;
        for (var i = 0; i < n; i++)
        {
            var maxDiff = 0;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(cpu[i * 4 + c] - gpu[i * 4 + c]);
                sum += d;
                maxDiff = Math.Max(maxDiff, d);
            }
            worst = Math.Max(worst, maxDiff);
            if (maxDiff > badThreshold)
                bad++;
        }
        var mean = sum / (double)(n * 3);
        Assert.True(mean <= meanLimit && bad <= n * badFraction,
            $"{label}: mean diff {mean:F2} (limit {meanLimit}), {bad}/{n} pixels over {badThreshold}, worst {worst}");
    }

    public static IEnumerable<object[]> EffectCases() =>
    [
        ["brightness", EffectCatalog.Brightness, P(("amount", ParamValue.OfDouble(0.3)))],
        ["brightness-dark", EffectCatalog.Brightness, P(("amount", ParamValue.OfDouble(-0.4)))],
        ["contrast", EffectCatalog.Contrast, P(("amount", ParamValue.OfDouble(1.6)))],
        ["grayscale", EffectCatalog.Grayscale, P(("amount", ParamValue.OfDouble(1)))],
        ["grayscale-part", EffectCatalog.Grayscale, P(("amount", ParamValue.OfDouble(0.6)))],
        ["saturation", EffectCatalog.Saturation, P(("amount", ParamValue.OfDouble(1.8)))],
        ["desaturate", EffectCatalog.Saturation, P(("amount", ParamValue.OfDouble(0.3)))],
        ["sepia", EffectCatalog.Sepia, P(("amount", ParamValue.OfDouble(0.8)))],
        ["invert", EffectCatalog.Invert, P(("amount", ParamValue.OfDouble(1)))],
        ["invert-half", EffectCatalog.Invert, P(("amount", ParamValue.OfDouble(0.4)))],
        ["hue", EffectCatalog.Hue, P(("degrees", ParamValue.OfDouble(120)))],
        ["tint", EffectCatalog.Tint, P(("color", ParamValue.OfColor(0xFFFF8000)), ("strength", ParamValue.OfDouble(0.6)),
            ("preserve_luma", ParamValue.OfBool(false)))],
        ["tint-luma", EffectCatalog.Tint, P(("color", ParamValue.OfColor(0xFF20C0FF)), ("strength", ParamValue.OfDouble(0.7)),
            ("preserve_luma", ParamValue.OfBool(true)))],
        ["vignette", EffectCatalog.Vignette, P(("strength", ParamValue.OfDouble(0.8)), ("color", ParamValue.OfColor(0xFF102030)))],
        ["posterize", EffectCatalog.Posterize, P(("levels", ParamValue.OfInt(5)))],
        ["pixelate", EffectCatalog.Pixelate, P(("block", ParamValue.OfInt(6)))],
        ["flip-h", EffectCatalog.Flip, P(("axis", ParamValue.OfChoice(0)))],
        ["flip-v", EffectCatalog.Flip, P(("axis", ParamValue.OfChoice(1)))],
        ["flip-both", EffectCatalog.Flip, P(("axis", ParamValue.OfChoice(2)))],
        ["sharpen", EffectCatalog.Sharpen, P(("amount", ParamValue.OfDouble(1.0)))],
        ["crop-pad", EffectCatalog.CropWithPadding, P(("left", ParamValue.OfDouble(0.1)), ("top", ParamValue.OfDouble(0.2)),
            ("right", ParamValue.OfDouble(0.15)), ("bottom", ParamValue.OfDouble(0.05)), ("color", ParamValue.OfColor(0xFF2040FF)))],
        ["transform-zoom", EffectCatalog.Transform, P(("centerX", ParamValue.OfDouble(0.6)), ("centerY", ParamValue.OfDouble(0.4)),
            ("zoom", ParamValue.OfDouble(1.8)), ("rotation", ParamValue.OfDouble(0)))],
        ["transform-rotate", EffectCatalog.Transform, P(("centerX", ParamValue.OfDouble(0.5)), ("centerY", ParamValue.OfDouble(0.5)),
            ("zoom", ParamValue.OfDouble(1.4)), ("rotation", ParamValue.OfDouble(25)))],
    ];

    [Theory]
    [MemberData(nameof(EffectCases))]
    public void Effect_matches_its_cpu_kernel(string label, string typeId, Dictionary<string, ParamValue> parameters)
    {
        var frame = MakeFrame(1);
        var cpu = EffectCatalog.Resolve(typeId)!.Apply(
            new DecodedFrame { Width = W, Height = H, Pixels = (byte[])frame.Pixels.Clone() }, parameters, 0).Pixels;

        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers =
            [
                new GpuLayer
                {
                    Source = new GpuSource { Image = ToImage(frame), Effects = [new GpuEffectCall(typeId, parameters)] },
                },
            ],
        };
        var gpu = GpuSceneRenderer.RenderToBgra(scene);

        // block-edge rounding on posterize/pixelate and the 1px sharpen kernel need a little more room
        AssertClose(cpu, gpu, label, meanLimit: 2.0, badFraction: 0.03);
    }

    public static IEnumerable<object[]> TransitionCases() =>
    [
        ["cross-dissolve", TransitionCatalog.CrossDissolve, 0.37, P()],
        ["fade-first-half", TransitionCatalog.FadeToBlack, 0.25, P()],
        ["fade-second-half", TransitionCatalog.FadeToBlack, 0.8, P()],
        ["wipe", TransitionCatalog.Wipe, 0.4, P(("soft", ParamValue.OfDouble(0.1)))],
        ["wipe-hard", TransitionCatalog.Wipe, 0.4, P(("soft", ParamValue.OfDouble(0)))],
        ["curtain", TransitionCatalog.Curtain, 0.45, P(("panels", ParamValue.OfInt(6)), ("soft", ParamValue.OfDouble(0.1)))],
        ["iris", TransitionCatalog.Iris, 0.5, P(("soft", ParamValue.OfDouble(0.1)))],
        ["push-left", TransitionCatalog.Push, 0.3, P(("direction", ParamValue.OfChoice(0)))],
        ["push-right", TransitionCatalog.Push, 0.3, P(("direction", ParamValue.OfChoice(1)))],
        ["push-up", TransitionCatalog.Push, 0.6, P(("direction", ParamValue.OfChoice(2)))],
        ["push-down", TransitionCatalog.Push, 0.6, P(("direction", ParamValue.OfChoice(3)))],
        ["slide-left", TransitionCatalog.Slide, 0.5, P(("direction", ParamValue.OfChoice(0)))],
        ["slide-right", TransitionCatalog.Slide, 0.5, P(("direction", ParamValue.OfChoice(1)))],
        ["slide-up", TransitionCatalog.Slide, 0.5, P(("direction", ParamValue.OfChoice(2)))],
        ["slide-down", TransitionCatalog.Slide, 0.5, P(("direction", ParamValue.OfChoice(3)))],
        ["swirl", TransitionCatalog.Swirl, 0.5, P()],
        ["swirl-ccw", TransitionCatalog.Swirl, 0.3, P(("direction", ParamValue.OfChoice(1)), ("rotation", ParamValue.OfDouble(200)),
            ("easing", ParamValue.OfChoice(0)))],
    ];

    [Theory]
    [MemberData(nameof(TransitionCases))]
    public void Transition_matches_its_cpu_blender(string label, string typeId, double t, Dictionary<string, ParamValue> parameters)
    {
        var a = MakeFrame(1);
        var b = MakeFrame(2);
        var blender = TransitionCatalog.Resolve(typeId)!;
        var cpu = blender.Blend(
            new DecodedFrame { Width = W, Height = H, Pixels = (byte[])a.Pixels.Clone() },
            new DecodedFrame { Width = W, Height = H, Pixels = (byte[])b.Pixels.Clone() }, t, parameters).Pixels;

        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers =
            [
                new GpuLayer
                {
                    Source = new GpuSource { Image = ToImage(a) },
                    Incoming = new GpuSource { Image = ToImage(b) },
                    TransitionTypeId = typeId,
                    Progress = t,
                    TransitionParams = parameters,
                },
            ],
        };
        var gpu = GpuSceneRenderer.RenderToBgra(scene);
        AssertClose(cpu, gpu, label, meanLimit: 2.0, badFraction: 0.03);
    }

    [Fact]
    public void Layers_blend_bottom_to_top_by_opacity_over_black()
    {
        var bottom = MakeFrame(1);
        var top = MakeFrame(2);
        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers =
            [
                new GpuLayer { Source = new GpuSource { Image = ToImage(bottom) } },
                new GpuLayer { Source = new GpuSource { Image = ToImage(top) }, Opacity = 0.5 },
            ],
        };
        var gpu = GpuSceneRenderer.RenderToBgra(scene);

        var expected = new byte[W * H * 4];
        var layers = new List<CompositeLayer>
        {
            new() { Frame = top, Opacity = 0.5 },     // CPU compositor takes topmost first
            new() { Frame = bottom, Opacity = 1 },
        };
        byte[]? scratch = null;
        FrameCompositor.ComposeInto(layers, W, H, expected, ref scratch);
        AssertClose(expected, gpu, "two layers");
    }

    [Fact]
    public void Crop_matches_the_cpu_compositor()
    {
        var frame = MakeFrame(1);
        var insets = new CropInsets(0.1, 0.25, 0.2, 0.05);
        var pixelCrop = new RectI(
            (int)Math.Round(insets.Left * W), (int)Math.Round(insets.Top * H),
            (int)Math.Round((1 - insets.Left - insets.Right) * W), (int)Math.Round((1 - insets.Top - insets.Bottom) * H));
        var cpu = new byte[W * H * 4];
        byte[]? scratch = null;
        FrameCompositor.ComposeInto([new CompositeLayer { Frame = frame, Crop = pixelCrop }], W, H, cpu, ref scratch);

        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers = [new GpuLayer { Source = new GpuSource { Image = ToImage(frame), Crop = insets } }],
        };
        AssertClose(cpu, GpuSceneRenderer.RenderToBgra(scene), "crop", meanLimit: 0.01, badFraction: 0.0001, badThreshold: 1);
    }

    [Fact]
    public void Transparent_letterbox_pixels_draw_as_black_like_the_cpu_compositor()
    {
        // decoded frames have alpha 0 in their letterbox bars; the CPU path ignores alpha, so they are black
        var white = new byte[W * H * 4];
        Array.Fill(white, (byte)255);
        var bars = new byte[W * H * 4];   // all zero: black with alpha 0
        var cpu = new byte[W * H * 4];
        byte[]? scratch = null;
        FrameCompositor.ComposeInto(
        [
            new CompositeLayer { Frame = new DecodedFrame { Width = W, Height = H, Pixels = bars }, Opacity = 0.5 },
            new CompositeLayer { Frame = new DecodedFrame { Width = W, Height = H, Pixels = white }, Opacity = 1 },
        ], W, H, cpu, ref scratch);

        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers =
            [
                new GpuLayer { Source = new GpuSource { Image = ToImage(new DecodedFrame { Width = W, Height = H, Pixels = white }) } },
                new GpuLayer { Source = new GpuSource { Image = ToImage(new DecodedFrame { Width = W, Height = H, Pixels = bars }) }, Opacity = 0.5 },
            ],
        };
        AssertClose(cpu, GpuSceneRenderer.RenderToBgra(scene), "alpha-0 bars", meanLimit: 1.0, badFraction: 0.001);
    }

    [Fact]
    public void Every_catalog_effect_renders_like_the_cpu_through_the_scene_builder()
    {
        // Guards new effects: one with a shader must match it, one without must fall back to the CPU
        // (and still show up) rather than vanish from the preview.
        foreach (var entry in EffectCatalog.All)
        {
            var clip = new VideoClip { DurSec = 5 };
            clip.Effects.Add(entry.CreateInstance());
            var frame = MakeFrame(1);
            var cpu = EffectPipeline.ApplyStack(
                new DecodedFrame { Width = W, Height = H, Pixels = (byte[])frame.Pixels.Clone() }, clip.Effects, 0).Pixels;

            using var scene = new GpuScene
            {
                Width = W,
                Height = H,
                Layers = [new GpuLayer { Source = GpuSceneBuilder.SourceFor(clip, frame, 0) }],
            };
            AssertClose(cpu, GpuSceneRenderer.RenderToBgra(scene), $"catalog effect '{entry.TypeId}'", meanLimit: 2.0, badFraction: 0.03);
        }
    }

    [Fact]
    public void Effect_without_a_shader_still_applies_via_cpu_fallback()
    {
        var clip = new VideoClip { DurSec = 5 };
        var fx = new EffectInstance { TypeId = "someFutureEffect" };
        clip.Effects.Add(fx);
        Assert.False(GpuEffects.Supports(fx.TypeId));

        // brightness is the only effect here, but dressed so the stack contains an unknown id first:
        // unknown ids are skipped by the CPU pipeline too, so the picture must come out unchanged
        var frame = MakeFrame(1);
        using var scene = new GpuScene
        {
            Width = W,
            Height = H,
            Layers = [new GpuLayer { Source = GpuSceneBuilder.SourceFor(clip, frame, 0) }],
        };
        AssertClose(frame.Pixels, GpuSceneRenderer.RenderToBgra(scene), "unknown effect", meanLimit: 0.01, badFraction: 0.0001, badThreshold: 1);
    }

    [Fact]
    public void Every_shader_compiles()
    {
        GpuEffects.CompileAll();
    }
}

public class CropOrientationTests
{
    /// <summary>Top half red, bottom half blue (BGRA, top-down like every decoded frame).</summary>
    private static DecodedFrame RedOverBlue(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                if (y < h / 2) px[i + 2] = 255; else px[i] = 255;
                px[i + 3] = 255;
            }
        return new DecodedFrame { Width = w, Height = h, Pixels = px };
    }

    [Fact]
    public void Cpu_top_inset_removes_the_top_of_the_picture()
    {
        const int w = 40, h = 40;
        var layer = new CompositeLayer { Frame = RedOverBlue(w, h), Crop = new RectI(0, h / 2, w, h / 2) };   // top inset 50%
        var pixels = new byte[w * h * 4];
        byte[]? scratch = null;
        FrameCompositor.ComposeInto([layer], w, h, pixels, ref scratch);

        // cropping away the top (red) half leaves the blue bottom half, scaled to fill the frame
        var mid = (h / 2 * w + w / 2) * 4;
        Assert.True(pixels[mid] > 200 && pixels[mid + 2] < 50, $"centre pixel B={pixels[mid]} R={pixels[mid + 2]}");
    }

    [Fact]
    public void Gpu_top_inset_removes_the_top_of_the_picture()
    {
        const int w = 40, h = 40;
        using var scene = new GpuScene
        {
            Width = w,
            Height = h,
            Layers =
            [
                new GpuLayer
                {
                    Source = new GpuSource
                    {
                        Image = SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque),
                            RedOverBlue(w, h).Pixels),
                        Crop = new CropInsets(0, 0.5, 0, 0),
                    },
                },
            ],
        };
        var pixels = GpuSceneRenderer.RenderToBgra(scene);
        var mid = (h / 2 * w + w / 2) * 4;
        Assert.True(pixels[mid] > 200 && pixels[mid + 2] < 50, $"centre pixel B={pixels[mid]} R={pixels[mid + 2]}");
    }
}
