using System.Text.Json;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class CreativeLibraryTests
{
    public static readonly string[] EffectIds = ["soft-blur", "directional-blur", "zoom-blur", "glow", "film-grain",
        "vhs", "digital-glitch", "camera-shake", "pulse-zoom", "strobe", "lens-distortion", "mirror", "light-leak", "duotone"];
    public static readonly string[] TransitionIds = ["white-flash", "blur-dissolve", "zoom-in", "zoom-out", "whip-pan",
        "spin", "glitch-cut", "pixel-dissolve", "luma-dissolve", "radial-wipe", "blinds", "card-flip"];
    public static IEnumerable<object[]> Effects() => EffectIds.Select(id => new object[] { id });
    public static IEnumerable<object[]> Transitions() => TransitionIds.Select(id => new object[] { id });

    private static DecodedFrame Picture(int seed, int width = 64, int height = 40)
    {
        byte[] pixels = new byte[width * height * 4];
        var random = new Random(seed);
        random.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new DecodedFrame { Width = width, Height = height, Pixels = pixels };
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void Effects_RenderWithoutMutatingInput_AndSurviveProjectSerialization(string id)
    {
        var entry = EffectCatalog.Find(id)!;
        Assert.NotNull(entry);
        var effect = JsonSerializer.Deserialize<EffectInstance>(JsonSerializer.Serialize(entry.CreateInstance()))!;
        var source = Picture(11);
        var original = (byte[])source.Pixels.Clone();
        var output = EffectCatalog.Resolve(id)!.Apply(source, effect.Params, id == "strobe" ? 0.03 : 0.137);
        try
        {
            Assert.Equal(source.Width, output.Width);
            Assert.Equal(source.Height, output.Height);
            Assert.Equal(original, source.Pixels);
            Assert.False(original.SequenceEqual(output.Pixels.Take(original.Length)), $"{id} had no visible effect");
            for (int i = 3; i < original.Length; i += 4) Assert.Equal(255, output.Pixels[i]);
        }
        finally { FramePool.Return(output.Pixels); }
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void Effects_ZeroAmountIsNeutral_AndTinyFramesAreSafe(string id)
    {
        var entry = EffectCatalog.Find(id)!;
        var p = entry.DefaultParams();
        if (p.ContainsKey("amount")) p["amount"] = ParamValue.OfDouble(0);
        var source = Picture(17, 1, 1);
        var output = EffectCatalog.Resolve(id)!.Apply(source, p, 0);
        try { Assert.Equal(source.Pixels, output.Pixels.Take(4)); }
        finally { FramePool.Return(output.Pixels); }
    }

    [Theory]
    [InlineData("film-grain")]
    [InlineData("vhs")]
    [InlineData("digital-glitch")]
    [InlineData("camera-shake")]
    [InlineData("pulse-zoom")]
    [InlineData("strobe")]
    [InlineData("light-leak")]
    public void AnimatedEffects_AreTimeVaryingButReproducible(string id)
    {
        var source = Picture(5);
        var processor = EffectCatalog.Resolve(id)!;
        var p = EffectCatalog.Find(id)!.DefaultParams();
        var a = processor.Apply(source, p, 0);
        var b = processor.Apply(source, p, 0);
        var later = processor.Apply(source, p, 0.137);
        try
        {
            int size = source.Width * source.Height * 4;
            Assert.Equal(a.Pixels.Take(size), b.Pixels.Take(size));
            Assert.False(a.Pixels.Take(size).SequenceEqual(later.Pixels.Take(size)));
        }
        finally { FramePool.Return(a.Pixels); FramePool.Return(b.Pixels); FramePool.Return(later.Pixels); }
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transitions_HaveExactEndpoints_AndRenderIntermediateFramesWithoutMutatingInputs(string id)
    {
        var entry = TransitionCatalog.Find(id)!;
        var blender = TransitionCatalog.Resolve(id)!;
        var parameters = JsonSerializer.Deserialize<TransitionRef>(JsonSerializer.Serialize(entry.CreateRef()))!.Params;
        var a = Picture(5);
        var b = Picture(7);
        var originalA = (byte[])a.Pixels.Clone();
        var originalB = (byte[])b.Pixels.Clone();
        Assert.Same(a, blender.Blend(a, b, 0, parameters));
        Assert.Same(b, blender.Blend(a, b, 1, parameters));
        foreach (double progress in new[] { 0.01, 0.25, 0.5, 0.75, 0.99 })
        {
            var output = blender.Blend(a, b, progress, parameters);
            try
            {
                Assert.Equal(a.Width, output.Width);
                Assert.Equal(a.Height, output.Height);
                for (int i = 3; i < originalA.Length; i += 4) Assert.Equal(255, output.Pixels[i]);
            }
            finally { FramePool.Return(output.Pixels); }
        }
        Assert.Equal(originalA, a.Pixels);
        Assert.Equal(originalB, b.Pixels);
        var small = Picture(3, 1, 1);
        var tiny = blender.Blend(small, small, 0.4, parameters);
        FramePool.Return(tiny.Pixels);
        Assert.Same(a, blender.Blend(a, small, 0.4, parameters));
    }

    [Fact]
    public void StandalonePipelineResult_RemainsOwnedAfterOtherRenders()
    {
        var source = Picture(3);
        var glow = EffectCatalog.Find("glow")!.CreateInstance();
        var output = EffectPipeline.ApplyStack(source, new[] { glow }, 0);
        var saved = (byte[])output.Pixels.Clone();
        for (int i = 0; i < 5; i++)
            EffectPipeline.ApplyStack(Picture(i + 5), new[] { glow }, i);
        Assert.Equal(saved, output.Pixels);
    }

    [Fact]
    public void ImpactPresets_HoldNeutralOutsideTheirAnimation()
    {
        foreach (var id in new[] { "white-flash-hit", "glow-hit", "glitch-burst", "shake-hit" })
        {
            var preset = BuiltInEffectPresets.Find(id)!;
            var fx = EffectPresetInstantiator.Create(preset, 10, 2);
            Assert.Equal(0, EffectPipeline.ResolveParams(fx, 0)["amount"].AsDouble);
            Assert.Equal(0, EffectPipeline.ResolveParams(fx, 9)["amount"].AsDouble);
            Assert.True(EffectPipeline.ResolveParams(fx, 2 + preset.DefaultDurationSec * 0.12)["amount"].AsDouble > 0);
        }
    }

    [Theory]
    [InlineData("pixel-dissolve")]
    [InlineData("luma-dissolve")]
    [InlineData("radial-wipe")]
    [InlineData("blinds")]
    public void SoftEdgeTransitions_ShowOnlyTheirOwnShotJustInsideTheEndpoints(string id)
    {
        var blender = TransitionCatalog.Resolve(id)!;
        var parameters = TransitionCatalog.Find(id)!.CreateRef().Params;
        var a = Picture(5);
        var b = Picture(7);
        var near0 = blender.Blend(a, b, 0.001, parameters);
        var near1 = blender.Blend(a, b, 0.999, parameters);
        try
        {
            int size = a.Width * a.Height * 4;
            Assert.Equal(a.Pixels.Take(size), near0.Pixels.Take(size));
            Assert.Equal(b.Pixels.Take(size), near1.Pixels.Take(size));
        }
        finally { FramePool.Return(near0.Pixels); FramePool.Return(near1.Pixels); }
    }

    [Fact]
    public void LensDistortion_Barrel_StaysInsideTheSourceFrame()
    {
        const int width = 200, height = 40;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                for (int c = 0; c < 4; c++)
                    pixels[(y * width + x) * 4 + c] = c == 3 ? (byte)255 : (byte)x;
        var source = new DecodedFrame { Width = width, Height = height, Pixels = pixels };
        var output = EffectCatalog.Resolve("lens-distortion")!.Apply(source, EffectCatalog.Find("lens-distortion")!.DefaultParams(), 0);
        try
        {
            int row = (height / 2) * width * 4;
            // Clamped edge pixels would read 0 and 199 here; a fitted barrel samples real picture.
            Assert.True(output.Pixels[row] > 10, $"left edge was {output.Pixels[row]}");
            Assert.True(output.Pixels[row + (width - 1) * 4] < 189, $"right edge was {output.Pixels[row + (width - 1) * 4]}");
        }
        finally { FramePool.Return(output.Pixels); }
    }

    [Fact]
    public void TiltBounce_ZoomAlwaysCoversTheRotatedFrame()
    {
        var fx = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("tilt-bounce")!, 3, 0);
        for (double t = 0; t <= 0.6; t += 0.005)
        {
            var p = EffectPipeline.ResolveParams(fx, t);
            double radians = Math.Abs(p["rotation"].AsDouble) * Math.PI / 180;
            double needed = Math.Cos(radians) + 16.0 / 9 * Math.Sin(radians);
            Assert.True(p["zoom"].AsDouble >= needed - 1e-9, $"t={t:F3} zoom {p["zoom"].AsDouble:F3} < {needed:F3}");
        }
    }

    [Fact]
    public void CatalogCategories_ComeFromTheAttributes()
    {
        Assert.Equal("Color", EffectCatalog.Find(EffectCatalog.Brightness)!.Category);
        Assert.Equal("Motion", EffectCatalog.Find(EffectCatalog.Transform)!.Category);
        Assert.Equal("Stylize", EffectCatalog.Find(EffectCatalog.Vignette)!.Category);
        Assert.Equal("Wipe", TransitionCatalog.Find(TransitionCatalog.Iris)!.Category);
        Assert.Equal("Camera", TransitionCatalog.Find(TransitionCatalog.Swirl)!.Category);
        Assert.Equal(0.35, TransitionCatalog.Find("white-flash")!.PreviewProgress);
        Assert.Equal(0.5, TransitionCatalog.Find(TransitionCatalog.CrossDissolve)!.PreviewProgress);
    }
}
