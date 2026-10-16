using Fig.App.Rendering;
using Fig.Core.Media;
using Fig.Core.Timeline;
using Xunit;

namespace Fig.App.Tests;

/// <summary>
/// Export hands a <see cref="RenderScene"/> to a renderer. The CPU renderer is the reference; the Skia one
/// (shaders, here on Skia's raster backend) must draw the same picture from the same scene.
/// </summary>
public class SceneRendererTests
{
    private const int W = 96, H = 50;

    private static DecodedFrame Frame(int seed)
    {
        var px = new byte[W * H * 4];
        var rng = new Random(seed);
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                px[i] = (byte)Math.Clamp(((x / 6 + y / 6) % 2 == 0 ? 40 : 210) + rng.Next(0, 20), 0, 255);
                px[i + 1] = (byte)(y * 255 / (H - 1));
                px[i + 2] = (byte)(x * 255 / (W - 1));
                px[i + 3] = 255;
            }
        return new DecodedFrame { Width = W, Height = H, Pixels = px };
    }

    private static EffectCall Call(string typeId, double t = 0.4, params (string, ParamValue)[] p)
    {
        var parameters = EffectCatalog.Find(typeId)!.DefaultParams();
        foreach (var (key, value) in p)
            parameters[key] = value;
        return new EffectCall(typeId, parameters, t);
    }

    private static void AssertSameAsCpu(RenderScene scene, string label, double meanLimit = 2.0, double badFraction = 0.03)
    {
        var cpu = new byte[W * H * 4];
        new CpuSceneRenderer().Render(scene, cpu);

        var skia = new byte[W * H * 4];
        new SkiaSceneRenderer(null).RenderAsync(scene, skia).GetAwaiter().GetResult();

        long sum = 0;
        var bad = 0;
        var worst = 0;
        for (var i = 0; i < W * H; i++)
        {
            var maxDiff = 0;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(cpu[i * 4 + c] - skia[i * 4 + c]);
                sum += d;
                maxDiff = Math.Max(maxDiff, d);
            }
            worst = Math.Max(worst, maxDiff);
            if (maxDiff > 10) bad++;
        }
        var mean = sum / (double)(W * H * 3);
        Assert.True(mean <= meanLimit && bad <= W * H * badFraction, $"{label}: mean {mean:F2}, {bad} pixels over 10, worst {worst}");
    }

    [Fact]
    public void A_clip_with_effects_and_a_crop_draws_the_same_on_both_renderers()
    {
        var scene = new RenderScene
        {
            Width = W, Height = H,
            Layers = [new RenderLayer
            {
                Source = new RenderSource
                {
                    Frame = Frame(1),
                    Effects = [Call(EffectCatalog.Sepia), Call("glow"), Call("film-grain", 0.7)],
                    Crop = new SceneCrop(0.1, 0.2, 0.05, 0.1),
                },
            }],
        };
        AssertSameAsCpu(scene, "effects + crop");
    }

    [Fact]
    public void Stacked_layers_with_opacity_and_track_effects_draw_the_same()
    {
        var scene = new RenderScene
        {
            Width = W, Height = H,
            Layers =
            [
                new RenderLayer { Source = new RenderSource { Frame = Frame(1) } },
                new RenderLayer
                {
                    Source = new RenderSource { Frame = Frame(2), Crop = new SceneCrop(0.2, 0, 0.2, 0) },
                    Effects = [Call("vhs", 1.2), Call(EffectCatalog.Vignette)],
                    Opacity = 0.6,
                },
                new RenderLayer { Source = new RenderSource { Frame = Frame(3), Effects = [Call("duotone")] }, Opacity = 0.35 },
            ],
        };
        AssertSameAsCpu(scene, "stack");
    }

    [Theory]
    [InlineData("cross-dissolve")]
    [InlineData("swirl")]
    [InlineData("glitch-cut")]
    [InlineData("blur-dissolve")]
    public void A_transition_with_effects_on_both_sides_and_the_track_draws_the_same(string transition)
    {
        var scene = new RenderScene
        {
            Width = W, Height = H,
            Layers = [new RenderLayer
            {
                Source = new RenderSource { Frame = Frame(1), Effects = [Call(EffectCatalog.Hue)], Crop = new SceneCrop(0.1, 0, 0, 0) },
                Incoming = new RenderSource { Frame = Frame(2), Effects = [Call("soft-blur")] },
                TransitionTypeId = transition,
                Progress = 0.45,
                TransitionParams = TransitionCatalog.Find(transition)!.CreateRef().Params,
                Effects = [Call(EffectCatalog.Contrast)],
                Opacity = 0.8,
            }],
        };
        AssertSameAsCpu(scene, transition);
    }

    [Fact]
    public void A_layer_with_something_the_gpu_cannot_run_is_drawn_on_the_cpu_not_dropped()
    {
        // a graph that does not compile has no shader; the CPU path passes the picture through
        var broken = new EffectGraph();
        broken.Nodes.Add(new GraphNode { Id = "a", PrimitiveId = "blur" });
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        try
        {
            library.Save(broken);
            Assert.False(GpuEffects.Supports(broken.TypeId));
            var scene = new RenderScene
            {
                Width = W, Height = H,
                Layers = [new RenderLayer
                {
                    Source = new RenderSource
                    {
                        Frame = Frame(1),
                        Effects = [new EffectCall(broken.TypeId, new Dictionary<string, ParamValue>(), 0), Call(EffectCatalog.Invert)],
                    },
                    Opacity = 0.7,
                }],
            };
            AssertSameAsCpu(scene, "cpu-baked layer", meanLimit: 0.6, badFraction: 0.001);
        }
        finally { library.Detach(); }
    }

    [Fact]
    public void An_empty_scene_is_black_on_both()
    {
        var scene = new RenderScene { Width = W, Height = H, Layers = [] };
        AssertSameAsCpu(scene, "empty", meanLimit: 0.01, badFraction: 0);
    }

    [Fact]
    public void A_user_graph_effect_in_a_scene_matches_the_cpu()
    {
        var graph = new EffectGraph { Name = "scene-graph" };
        graph.Nodes.Add(new GraphNode { Id = "in", PrimitiveId = "input" });
        graph.Nodes.Add(new GraphNode { Id = "b", PrimitiveId = "blur", Params = { ["radius"] = ParamValue.OfDouble(0.03) } });
        graph.Nodes.Add(new GraphNode { Id = "d", PrimitiveId = "duotone" });
        graph.Nodes.Add(new GraphNode { Id = "out", PrimitiveId = "output" });
        graph.Edges.Add(new GraphEdge { FromNodeId = "in", FromPort = "image", ToNodeId = "b", ToPort = "image" });
        graph.Edges.Add(new GraphEdge { FromNodeId = "b", FromPort = "image", ToNodeId = "d", ToPort = "image" });
        graph.Edges.Add(new GraphEdge { FromNodeId = "d", FromPort = "image", ToNodeId = "out", ToPort = "image" });
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        try
        {
            Assert.True(library.Save(graph).IsValid);
            var scene = new RenderScene
            {
                Width = W, Height = H,
                Layers = [new RenderLayer
                {
                    Source = new RenderSource { Frame = Frame(4), Effects = [new EffectCall(graph.TypeId, EffectCatalog.Find(graph.TypeId)!.DefaultParams(), 0.3)] },
                }],
            };
            AssertSameAsCpu(scene, "graph in scene", meanLimit: 1.5, badFraction: 0.02);
        }
        finally { library.Detach(); }
    }
}

public class SkiaSceneRendererRobustnessTests
{
    private const int W = 64, H = 36;

    private static RenderScene Scene(int seed)
    {
        var px = new byte[W * H * 4];
        new Random(seed).NextBytes(px);
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
        return new RenderScene
        {
            Width = W, Height = H,
            Layers = [new RenderLayer
            {
                Source = new RenderSource
                {
                    Frame = new DecodedFrame { Width = W, Height = H, Pixels = px },
                    Effects = [new EffectCall(EffectCatalog.Invert, EffectCatalog.Find(EffectCatalog.Invert)!.DefaultParams(), 0)],
                },
            }],
        };
    }

    private sealed class FakeRunner : IGpuJobRunner
    {
        public int Calls;
        public bool Fail;

        public Task<T> Run<T>(Func<SkiaSharp.GRContext?, T> job, TimeSpan? timeout = null)
        {
            Calls++;
            if (Fail)
                return Task.FromException<T>(new TimeoutException("no draw"));
            return Task.FromResult(job(null));   // a software canvas: the same code on Skia's raster backend
        }
    }

    [Fact]
    public async Task Frames_are_rendered_through_the_runner_into_the_callers_canvas()
    {
        var runner = new FakeRunner();
        var renderer = new SkiaSceneRenderer(runner);
        var scene = Scene(1);
        var expected = new byte[W * H * 4];
        new CpuSceneRenderer().Render(scene, expected);

        var canvas = new byte[W * H * 4];
        await renderer.RenderAsync(scene, canvas);

        Assert.Equal(1, runner.Calls);
        Assert.True(renderer.MaxInFlight > 1, "the GPU renderer should pipeline frames");
        var worst = Enumerable.Range(0, W * H * 4).Where(i => i % 4 != 3).Max(i => Math.Abs(expected[i] - canvas[i]));
        Assert.True(worst <= 2, $"worst channel difference {worst}");
    }

    [Fact]
    public async Task When_the_gpu_fails_the_frame_is_still_drawn_and_later_frames_skip_the_gpu()
    {
        var runner = new FakeRunner { Fail = true };
        var renderer = new SkiaSceneRenderer(runner);
        var first = Scene(1);
        var expectedFirst = new byte[W * H * 4];
        new CpuSceneRenderer().Render(first, expectedFirst);

        var canvas = new byte[W * H * 4];
        await renderer.RenderAsync(first, canvas);
        Assert.Equal(1, runner.Calls);
        Assert.True(Enumerable.Range(0, W * H * 4).Where(i => i % 4 != 3).Max(i => Math.Abs(expectedFirst[i] - canvas[i])) <= 2,
            "the failed frame should still be drawn");

        var second = Scene(2);
        var expectedSecond = new byte[W * H * 4];
        new CpuSceneRenderer().Render(second, expectedSecond);
        await renderer.RenderAsync(second, canvas);
        Assert.Equal(1, runner.Calls);   // the GPU was not asked again
        Assert.Equal(expectedSecond, canvas);   // the CPU kernels drew it, bit for bit
    }
}
