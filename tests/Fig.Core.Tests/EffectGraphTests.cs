using System.Text.Json;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class EffectGraphTests
{
    // ---- builders ---------------------------------------------------------------------------

    private sealed class Builder
    {
        public EffectGraph Graph { get; } = new() { Name = "Test" };

        public string Node(string primitive, string id, Dictionary<string, ParamValue>? p = null, string? effect = null, bool bypass = false)
        {
            Graph.Nodes.Add(new GraphNode { Id = id, PrimitiveId = primitive, Params = p ?? new(), EffectTypeId = effect, Bypass = bypass });
            return id;
        }

        public Builder Wire(string from, string fromPort, string to, string toPort)
        {
            Graph.Edges.Add(new GraphEdge { FromNodeId = from, FromPort = fromPort, ToNodeId = to, ToPort = toPort });
            return this;
        }

        public Builder Promote(string key, string node, string param, double scale = 1, double? def = null)
        {
            Graph.Promoted.Add(new PromotedParam
            {
                Key = key, Label = key,
                Targets = { new PromotedTarget { NodeId = node, Param = param, Scale = scale } },
                Default = def,
            });
            return this;
        }
    }

    private static Dictionary<string, ParamValue> P(string key, double value) => new() { [key] = ParamValue.OfDouble(value) };

    private static DecodedFrame Picture(int seed = 1, int width = 32, int height = 20)
    {
        var pixels = new byte[width * height * 4];
        new Random(seed).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new DecodedFrame { Width = width, Height = height, Pixels = pixels };
    }

    private static byte[] Bytes(DecodedFrame f) => f.Pixels.Take(f.Width * f.Height * 4).ToArray();

    private static IReadOnlyDictionary<string, ParamValue> None => new Dictionary<string, ParamValue>();

    private static Builder InputToOutput(string middle, Dictionary<string, ParamValue>? p = null)
    {
        var b = new Builder();
        b.Node("input", "in");
        b.Node(middle, "mid", p);
        b.Node("output", "out");
        b.Wire("in", "image", "mid", "image").Wire("mid", "image", "out", "image");
        return b;
    }

    // ---- compile ----------------------------------------------------------------------------

    [Fact]
    public void Compile_ValidGraph_OrdersNodesByDependency()
    {
        var b = InputToOutput("blur");
        var result = GraphCompiler.Compile(b.Graph);
        Assert.True(result.IsValid);
        Assert.Equal(new[] { "in", "mid", "out" }, result.Plan!.Steps.Select(s => s.Node.Id));
    }

    [Fact]
    public void Compile_NodesThatDoNotFeedTheOutput_AreReportedNotFatal()
    {
        var b = InputToOutput("blur");
        b.Node("blur", "stray"); // required input unconnected, but it does not matter: nothing reads it
        var result = GraphCompiler.Compile(b.Graph);
        Assert.True(result.IsValid);
        Assert.Equal(new[] { "stray" }, result.UnusedNodeIds);
        Assert.DoesNotContain(result.Plan!.Steps, s => s.Node.Id == "stray");
    }

    [Fact]
    public void Compile_ReportsEachKindOfProblemOnTheRightNode()
    {
        // no output
        var none = new Builder();
        none.Node("input", "in");
        Assert.Contains(GraphCompiler.Compile(none.Graph).Errors, e => e.Code == GraphErrorCode.NoOutput);

        // two outputs
        var two = InputToOutput("blur");
        two.Node("output", "out2");
        two.Wire("mid", "image", "out2", "image");
        Assert.Contains(GraphCompiler.Compile(two.Graph).Errors, e => e.Code == GraphErrorCode.MultipleOutputs && e.NodeId == "out2");

        // missing input on a node that matters
        var missing = new Builder();
        missing.Node("blur", "mid");
        missing.Node("output", "out");
        missing.Wire("mid", "image", "out", "image");
        Assert.Contains(GraphCompiler.Compile(missing.Graph).Errors, e => e.Code == GraphErrorCode.MissingInput && e.NodeId == "mid");

        // mask into an image input
        var mismatch = new Builder();
        mismatch.Node("noise", "noise");
        mismatch.Node("output", "out");
        mismatch.Wire("noise", "mask", "out", "image");
        Assert.Contains(GraphCompiler.Compile(mismatch.Graph).Errors, e => e.Code == GraphErrorCode.TypeMismatch && e.NodeId == "out");

        // two wires into one input
        var twice = InputToOutput("blur");
        twice.Node("solid", "solid");
        twice.Wire("solid", "image", "mid", "image");
        Assert.Contains(GraphCompiler.Compile(twice.Graph).Errors, e => e.Code == GraphErrorCode.InputAlreadyConnected && e.NodeId == "mid");

        // unknown primitive
        var unknown = InputToOutput("not-a-node");
        Assert.Contains(GraphCompiler.Compile(unknown.Graph).Errors, e => e.Code == GraphErrorCode.UnknownPrimitive && e.NodeId == "mid");

        // a wire to a node that is gone
        var dangling = InputToOutput("blur");
        dangling.Wire("ghost", "image", "mid", "image");
        Assert.Contains(GraphCompiler.Compile(dangling.Graph).Errors, e => e.Code == GraphErrorCode.BadEdge);
    }

    [Fact]
    public void Compile_RejectsLoops()
    {
        var b = new Builder();
        b.Node("blur", "a");
        b.Node("blur", "b");
        b.Node("output", "out");
        b.Wire("a", "image", "b", "image").Wire("b", "image", "a", "image").Wire("b", "image", "out", "image");
        Assert.Contains(GraphCompiler.Compile(b.Graph).Errors, e => e.Code == GraphErrorCode.Cycle);
    }

    [Fact]
    public void Compile_StockEffectNode_NeedsARealEffect_AndCannotNestGraphs()
    {
        var ok = InputToOutput("effect");
        ok.Graph.Nodes[1].EffectTypeId = EffectCatalog.Sepia;
        Assert.True(GraphCompiler.Compile(ok.Graph).IsValid);

        var none = InputToOutput("effect");
        Assert.Contains(GraphCompiler.Compile(none.Graph).Errors, e => e.Code == GraphErrorCode.UnknownEffect);

        var nested = InputToOutput("effect");
        nested.Graph.Nodes[1].EffectTypeId = "graph:other";
        Assert.Contains(GraphCompiler.Compile(nested.Graph).Errors, e => e.Code == GraphErrorCode.UnknownEffect);
    }

    [Fact]
    public void Compile_PromotedControls_MustPointAtRealParameters_WithUniqueKeys()
    {
        var bad = InputToOutput("blur").Promote("amount", "mid", "no-such-param");
        Assert.Contains(GraphCompiler.Compile(bad.Graph).Errors, e => e.Code == GraphErrorCode.BadPromotedParam);

        var dup = InputToOutput("blur").Promote("amount", "mid", "radius").Promote("amount", "mid", "radius");
        Assert.Contains(GraphCompiler.Compile(dup.Graph).Errors, e => e.Code == GraphErrorCode.BadPromotedParam);

        var good = InputToOutput("blur").Promote("amount", "mid", "radius");
        Assert.True(GraphCompiler.Compile(good.Graph).IsValid);
    }

    // ---- rendering --------------------------------------------------------------------------

    [Fact]
    public void Apply_IdentityGraph_ReturnsTheSameFrame()
    {
        var b = new Builder();
        b.Node("input", "in");
        b.Node("output", "out");
        b.Wire("in", "image", "out", "image");
        var source = Picture();
        var before = Bytes(source);

        var output = new GraphEffectProcessor(b.Graph).Apply(source, None, 0);

        Assert.Same(source, output);
        Assert.Equal(before, Bytes(source));
    }

    [Fact]
    public void Apply_StockEffectNode_MatchesRunningTheEffectDirectly()
    {
        var b = InputToOutput("effect");
        b.Graph.Nodes[1].EffectTypeId = EffectCatalog.Sepia;
        var source = Picture(4);
        var original = Bytes(source);

        var viaGraph = new GraphEffectProcessor(b.Graph).Apply(source, None, 0);
        var direct = EffectCatalog.Resolve(EffectCatalog.Sepia)!.Apply(source, EffectCatalog.Find(EffectCatalog.Sepia)!.DefaultParams(), 0);
        try
        {
            Assert.Equal(Bytes(direct), Bytes(viaGraph));
            Assert.Equal(original, Bytes(source));
        }
        finally { FramePool.Return(viaGraph.Pixels); FramePool.Return(direct.Pixels); }
    }

    [Fact]
    public void Apply_PromotedControl_DrivesTheNodeParameter()
    {
        var b = InputToOutput("color-matrix").Promote("lift", "mid", "brightness");
        var processor = new GraphEffectProcessor(b.Graph);
        var source = Picture(8);

        var neutral = processor.Apply(source, None, 0);
        var lifted = processor.Apply(source, P("lift", 0.5), 0);
        try
        {
            Assert.Equal(Bytes(source), Bytes(neutral)); // brightness 0 is a pass-through
            Assert.NotEqual(Bytes(source), Bytes(lifted));
            // the same node run with the value set directly gives the same picture
            var direct = InputToOutput("color-matrix", P("brightness", 0.5));
            var expected = new GraphEffectProcessor(direct.Graph).Apply(source, None, 0);
            Assert.Equal(Bytes(expected), Bytes(lifted));
            FramePool.Return(expected.Pixels);
        }
        finally { FramePool.Return(lifted.Pixels); }
    }

    [Fact]
    public void Apply_PromotedScale_LetsOneControlDriveTwoNodesInOppositeDirections()
    {
        var b = new Builder();
        b.Node("input", "in");
        b.Node("uv-offset", "a");
        b.Node("uv-offset", "c");
        b.Node("mix", "mix", P("amount", 1));
        b.Node("output", "out");
        b.Wire("in", "image", "a", "image").Wire("in", "image", "c", "image")
            .Wire("a", "image", "mix", "a").Wire("c", "image", "mix", "b").Wire("mix", "image", "out", "image");
        b.Graph.Promoted.Add(new PromotedParam
        {
            Key = "shift", Label = "Shift",
            Targets = { new PromotedTarget { NodeId = "a", Param = "x" }, new PromotedTarget { NodeId = "c", Param = "x", Scale = -1 } },
        });
        var source = Picture(2, 40, 8);

        var output = new GraphEffectProcessor(b.Graph).Apply(source, P("shift", 0.1), 0);
        // mix amount 1 shows B, which was shifted the opposite way: it equals A shifted by -0.1
        var left = InputToOutput("uv-offset", P("x", -0.1));
        var expected = new GraphEffectProcessor(left.Graph).Apply(source, None, 0);
        try { Assert.Equal(Bytes(expected), Bytes(output)); }
        finally { FramePool.Return(output.Pixels); FramePool.Return(expected.Pixels); }
    }

    [Fact]
    public void Apply_SplitAndMerge_RoundTripsThePicture_AndAShiftTouchesOnlyOneChannel()
    {
        Builder Build(double shift)
        {
            var b = new Builder();
            b.Node("input", "in");
            b.Node("split-channels", "split");
            b.Node("uv-offset", "shift", P("x", shift));
            b.Node("merge-channels", "merge");
            b.Node("output", "out");
            b.Wire("in", "image", "split", "image")
                .Wire("split", "r", "shift", "image").Wire("shift", "image", "merge", "r")
                .Wire("split", "g", "merge", "g").Wire("split", "b", "merge", "b")
                .Wire("merge", "image", "out", "image");
            return b;
        }
        var source = Picture(6, 48, 16);

        var identity = new GraphEffectProcessor(Build(0).Graph).Apply(source, None, 0);
        var shifted = new GraphEffectProcessor(Build(0.2).Graph).Apply(source, None, 0);
        try
        {
            Assert.Equal(Bytes(source), Bytes(identity));
            var s = Bytes(source); var o = Bytes(shifted);
            bool redChanged = false;
            for (int i = 0; i < s.Length; i += 4)
            {
                Assert.Equal(s[i], o[i]);         // blue untouched
                Assert.Equal(s[i + 1], o[i + 1]); // green untouched
                redChanged |= s[i + 2] != o[i + 2];
            }
            Assert.True(redChanged);
        }
        finally { FramePool.Return(identity.Pixels); FramePool.Return(shifted.Pixels); }
    }

    [Fact]
    public void Apply_Mix_MaskControlsWhereTheSecondPictureShows()
    {
        Builder Build(uint maskColor)
        {
            var b = new Builder();
            b.Node("input", "in");
            b.Node("solid", "white", new() { ["color"] = ParamValue.OfColor(0xFFFFFFFF) });
            b.Node("solid", "gate", new() { ["color"] = ParamValue.OfColor(maskColor) });
            b.Node("mask-luma", "mask", new() { ["softness"] = ParamValue.OfDouble(0.05) });
            b.Node("mix", "mix", P("amount", 1));
            b.Node("output", "out");
            b.Wire("in", "image", "mix", "a").Wire("white", "image", "mix", "b")
                .Wire("gate", "image", "mask", "image").Wire("mask", "mask", "mix", "mask")
                .Wire("mix", "image", "out", "image");
            return b;
        }
        var source = Picture(3);

        var closed = new GraphEffectProcessor(Build(0xFF000000).Graph).Apply(source, None, 0);
        var open = new GraphEffectProcessor(Build(0xFFFFFFFF).Graph).Apply(source, None, 0);
        try
        {
            Assert.Equal(Bytes(source), Bytes(closed)); // black gate: A only
            Assert.All(Bytes(open).Where((_, i) => i % 4 != 3), v => Assert.Equal(255, v)); // white gate: B only
        }
        finally { FramePool.Return(closed.Pixels); FramePool.Return(open.Pixels); }
    }

    [Fact]
    public void Apply_BypassedNode_PassesItsInputThrough()
    {
        var b = InputToOutput("color-matrix", P("brightness", 0.8));
        b.Graph.Nodes[1].Bypass = true;
        var source = Picture(9);
        var output = new GraphEffectProcessor(b.Graph).Apply(source, None, 0);
        Assert.Same(source, output);
    }

    [Fact]
    public void Apply_InvalidGraph_FailsSafeAndReturnsTheInput()
    {
        var b = new Builder();
        b.Node("blur", "a"); // no output
        var processor = new GraphEffectProcessor(b.Graph);
        var source = Picture();
        Assert.False(processor.Compile.IsValid);
        Assert.Same(source, processor.Apply(source, None, 0));
    }

    [Fact]
    public void Apply_IsRepeatable_AndNeverMutatesTheInput()
    {
        var b = new Builder();
        b.Node("input", "in");
        b.Node("blur", "blur", P("radius", 0.05));
        b.Node("duotone", "duo");
        b.Node("noise", "noise");
        b.Node("mix", "mix", P("amount", 0.6));
        b.Node("output", "out");
        b.Wire("in", "image", "blur", "image").Wire("blur", "image", "duo", "image")
            .Wire("in", "image", "mix", "a").Wire("duo", "image", "mix", "b").Wire("noise", "mask", "mix", "mask")
            .Wire("mix", "image", "out", "image");
        var processor = new GraphEffectProcessor(b.Graph);
        var source = Picture(11, 64, 36);
        var original = Bytes(source);

        var first = Bytes(processor.Apply(source, None, 0.5));
        for (int i = 0; i < 4; i++)
        {
            var again = processor.Apply(source, None, 0.5);
            Assert.Equal(first, Bytes(again));
            FramePool.Return(again.Pixels);
        }
        Assert.Equal(original, Bytes(source));
    }

    // ---- library & catalog ------------------------------------------------------------------

    private static EffectGraph Soft(string name = "Soft glow")
    {
        var b = InputToOutput("blur", P("radius", 0.03));
        b.Promote("softness", "mid", "radius");
        b.Graph.Name = name;
        return b.Graph;
    }

    [Fact]
    public void Library_SavedGraph_AppearsInTheCatalogWithItsPromotedControls()
    {
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        try
        {
            var graph = Soft();
            Assert.True(library.Save(graph).IsValid);

            var entry = EffectCatalog.Find(graph.TypeId)!;
            Assert.Equal(GraphEffectLibrary.Category, entry.Category);
            Assert.True(entry.IsCustom);
            Assert.Equal("Soft glow", entry.DisplayName);
            var def = Assert.Single(entry.ParamSchema);
            Assert.Equal("softness", def.Key);
            Assert.Equal(0.03, def.Default, 6);   // the node's current value is the default
            Assert.Equal(0.08, def.Max, 6);       // range comes from the node parameter
            Assert.NotNull(EffectCatalog.Resolve(graph.TypeId));
            Assert.Contains(EffectCatalog.All, e => e.TypeId == graph.TypeId);
            Assert.DoesNotContain(EffectCatalog.All.Where(e => !e.IsCustom), e => e.TypeId == graph.TypeId);
        }
        finally { library.Detach(); }
    }

    [Fact]
    public void Library_GraphEffect_RunsThroughTheNormalEffectPipeline_WithKeyframes()
    {
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        try
        {
            var graph = Soft();
            library.Save(graph);
            var fx = EffectCatalog.Find(graph.TypeId)!.CreateInstance();
            fx.Keyframes["softness"] = new List<KeyframePoint>
            {
                new(0, ParamValue.OfDouble(0)),
                new(2, ParamValue.OfDouble(0.08)),
            };
            var source = Picture(5, 64, 36);

            var atStart = EffectPipeline.ApplyStack(source, new[] { fx }, 0);
            var later = EffectPipeline.ApplyStack(source, new[] { fx }, 2);

            Assert.Equal(Bytes(source), Bytes(atStart));   // blur radius 0 is neutral
            Assert.NotEqual(Bytes(source), Bytes(later));  // the keyframed control reached the node
        }
        finally { library.Detach(); }
    }

    [Fact]
    public void Library_RejectsInvalidGraphs_AndDeleteAndDetachUnpublish()
    {
        var library = new GraphEffectLibrary();
        var list = new List<EffectGraph>();
        library.Attach(list);
        var broken = new EffectGraph { Name = "Broken" };
        broken.Nodes.Add(new GraphNode { Id = "a", PrimitiveId = "blur" });
        Assert.False(library.Save(broken).IsValid);
        Assert.Empty(list);

        var graph = Soft();
        library.Save(graph);
        Assert.Single(list);
        Assert.NotNull(EffectCatalog.Find(graph.TypeId));

        library.Delete(graph.Id);
        Assert.Empty(list);
        Assert.Null(EffectCatalog.Find(graph.TypeId));

        library.Save(graph);
        library.Detach();
        Assert.Null(EffectCatalog.Find(graph.TypeId));
    }

    [Fact]
    public void Library_ResavingReplacesTheGraph_AndStoresACopy()
    {
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        try
        {
            var graph = Soft("First");
            library.Save(graph);
            graph.Name = "Second";
            graph.Nodes[1].Params["radius"] = ParamValue.OfDouble(0.07);
            Assert.Equal("First", library.Find(graph.Id)!.Name); // editing the working copy changes nothing saved
            library.Save(graph);

            Assert.Single(library.Graphs);
            Assert.Equal("Second", EffectCatalog.Find(graph.TypeId)!.DisplayName);
            Assert.Equal(0.07, EffectCatalog.Find(graph.TypeId)!.ParamSchema[0].Default, 6);
        }
        finally { library.Detach(); }
    }

    [Fact]
    public void Library_ExportAndImport_RoundTripTheGraph_AndAvoidIdClashes()
    {
        var library = new GraphEffectLibrary();
        library.Attach(new List<EffectGraph>());
        var path = Path.Combine(Path.GetTempPath(), $"fig-graph-{Guid.NewGuid():N}.figgraph.json");
        try
        {
            var graph = Soft();
            library.Save(graph);
            library.Export(graph.Id, path);

            var imported = library.Import(path);
            Assert.NotEqual(graph.Id, imported.Id);       // the id was taken, so it became a new effect
            Assert.Equal(graph.Name, imported.Name);
            Assert.Equal(2, library.Graphs.Count);
            Assert.Equal(graph.Promoted[0].Key, imported.Promoted[0].Key);
            Assert.Equal(0.03, imported.Nodes[1].Params["radius"].AsDouble, 6);
        }
        finally { library.Detach(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Graph_SerializesWithTheProject()
    {
        var project = Fig.Core.Project.Project.Create("p");
        project.EffectGraphs.Add(Soft());
        var json = JsonSerializer.Serialize(project);
        var back = JsonSerializer.Deserialize<Fig.Core.Project.Project>(json)!;
        var graph = Assert.Single(back.EffectGraphs);
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
        Assert.Equal("softness", graph.Promoted[0].Key);
    }

    [Fact]
    public void Primitives_AreDiscovered_WithPortsAndSchemas()
    {
        var mix = PrimitiveCatalog.Find("mix")!;
        Assert.Equal(new[] { "a", "b", "mask" }, mix.Inputs.Select(p => p.Id));
        Assert.False(mix.Input("mask")!.Required);
        Assert.Equal(GraphPortType.Mask, mix.Input("mask")!.Type);
        Assert.Equal(new[] { "r", "g", "b" }, PrimitiveCatalog.Find("split-channels")!.Outputs.Select(p => p.Id));
        Assert.NotNull(PrimitiveCatalog.Find(PrimitiveCatalog.Output));
        Assert.True(PrimitiveCatalog.All.Count >= 15);
        Assert.All(PrimitiveCatalog.All, d => Assert.NotNull(PrimitiveCatalog.Resolve(d.Id)));
    }
}
