using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// Runs a user's effect graph as shaders. The graph is walked in its compiled order exactly like the CPU
/// executor, but every node produces a shader instead of a picture, so a whole graph becomes one draw.
/// Where a result is read by several nodes it is rendered once into an offscreen picture and shared.
/// </summary>
internal static partial class GpuEffects
{
    private static readonly HashSet<string> GraphPrimitiveIds =
    [
        "input", "solid", "noise", "effect", "blur", "directional-blur", "mix", "mask-luma", "uv-offset",
        "lens-warp", "mirror", "split-channels", "merge-channels", "color-matrix", "levels", "duotone",
    ];

    /// <summary>True when <paramref name="typeId"/> names a valid graph effect whose every node has a shader.</summary>
    public static bool SupportsGraph(string typeId)
    {
        if (!EffectGraph.IsGraphTypeId(typeId) || EffectCatalog.Resolve(typeId) is not GraphEffectProcessor { Plan: { } plan })
            return false;
        foreach (var step in plan.Steps)
        {
            if (step.IsOutput || step.Bypassed)
                continue;
            var id = step.Node.PrimitiveId;
            if (!GraphPrimitiveIds.Contains(id))
                return false;
            if (id == "effect" && (step.Node.EffectTypeId is not { } stock || !Supports(stock)))
                return false;
        }
        return true;
    }

    private static SKShader ApplyGraph(SKShader input, EffectCall call, int w, int h, ShaderScope scope)
    {
        if (EffectCatalog.Resolve(call.TypeId) is not GraphEffectProcessor { Plan: { } plan } processor)
            return input;
        var nodeParams = processor.ResolveNodeParams(call.Params);
        var values = new Dictionary<(string, string), SKShader>();
        foreach (var step in plan.Steps)
        {
            var inputs = step.Inputs.ToDictionary(kv => kv.Key, kv => values[kv.Value]);
            if (step.IsOutput)
                return inputs["image"];

            Dictionary<string, SKShader> produced;
            if (step.Bypassed)
                produced = new() { [step.Descriptor.Outputs[0].Id] = inputs[step.Descriptor.Inputs[0].Id] };
            else
            {
                var context = PrimitiveContext.ForParameters(step.Descriptor, step.Node, nodeParams[step.Node.Id], call.LocalT);
                produced = RunPrimitive(step, context, inputs, input, call.LocalT, w, h, scope);
            }

            foreach (var output in step.Descriptor.Outputs)
            {
                if (!produced.TryGetValue(output.Id, out var shader))
                    continue;
                // an output several nodes read is drawn once instead of once per reader
                if (plan.Consumers.GetValueOrDefault((step.Node.Id, output.Id)) > 1 && step.Node.PrimitiveId != "input")
                    shader = scope.Materialize(shader, w, h);
                values[(step.Node.Id, output.Id)] = shader;
            }
        }
        return input;
    }

    private static Dictionary<string, SKShader> RunPrimitive(PlanStep step, PrimitiveContext c,
        Dictionary<string, SKShader> inputs, SKShader source, double t, int w, int h, ShaderScope scope)
    {
        var size = Pair(w, h);
        Dictionary<string, SKShader> One(SKShader s) => new() { ["image"] = s };

        switch (step.Node.PrimitiveId)
        {
            case "input":
                return One(source);
            case "solid":
            {
                var color = Rgb(c.Color("color"));
                return One(Run(Get("g.solid", Sources["g.solid"]), scope, u => u["color"] = color));
            }
            case "noise":
            {
                var cell = (float)Math.Max(1, w / Math.Max(2, c.Int("scale")));
                var seed = (float)Math.Floor(t * c.Number("speed"));
                return new() { ["mask"] = Run(Get("g.noise", Sources["g.noise"]), scope, u => { u["cell"] = cell; u["seed"] = seed; }) };
            }
            case "effect":
            {
                var src = inputs["image"];
                var typeId = step.Node.EffectTypeId;
                if (typeId is null || EffectGraph.IsGraphTypeId(typeId))
                    return One(src);
                var parameters = new Dictionary<string, ParamValue>();
                if (EffectCatalog.Find(typeId) is { } entry)
                    foreach (var (key, value) in entry.DefaultParams())
                        parameters[key] = value;
                foreach (var (key, value) in c.Params)
                    parameters[key] = value;
                return One(Apply(src, new EffectCall(typeId, parameters, t), w, h, scope));
            }
            case "blur":
            {
                var src = inputs["image"];
                var radius = (int)Math.Round(c.Number("radius") * Math.Min(w, h));
                return One(radius <= 0 ? src : BoxBlur(src, radius, w, h, scope));
            }
            case "directional-blur":
            {
                var src = inputs["image"];
                var distance = c.Number("distance") * w;
                if (distance <= 0)
                    return One(src);
                var angle = c.Number("angle") * Math.PI / 180;
                var stepv = Pair(Math.Cos(angle) * distance / 4, Math.Sin(angle) * distance / 4);
                return One(Run(Get("dirBlur", Sources["dirBlur"]), scope, u => { u["size"] = size; u["stepv"] = stepv; }, ("src", src)));
            }
            case "mix":
            {
                SKShader a = inputs["a"], b = inputs["b"];
                var mask = inputs.GetValueOrDefault("mask");
                var amount = c.Number("amount");
                if (mask is null && amount <= 0)
                    return One(a);
                var mode = c.Choice("mode");
                return One(Run(Get("g.mix", Sources["g.mix"]), scope, u =>
                {
                    u["amount"] = (float)amount;
                    u["mode"] = (float)mode;
                    u["useMask"] = mask is null ? 0f : 1f;
                }, ("a", a), ("b", b), ("mask", mask ?? a)));
            }
            case "mask-luma":
            {
                var threshold = c.Number("threshold");
                var softness = c.Number("softness");
                var invert = c.Bool("invert");
                return new()
                {
                    ["mask"] = Run(Get("g.maskLuma", Sources["g.maskLuma"]), scope, u =>
                    {
                        u["threshold"] = (float)threshold;
                        u["softness"] = (float)softness;
                        u["invert"] = invert ? 1f : 0f;
                    }, ("src", inputs["image"])),
                };
            }
            case "uv-offset":
            {
                double ox = c.Number("x"), oy = c.Number("y");
                if (ox == 0 && oy == 0)
                    return One(inputs["image"]);
                var edge = c.Choice("edge");
                return One(Run(Get("g.uv", Sources["g.uv"]), scope, u =>
                {
                    u["size"] = size;
                    u["offset"] = Pair(ox, oy);
                    u["edge"] = (float)edge;
                }, ("src", inputs["image"])));
            }
            case "lens-warp":
            {
                var amount = c.Number("amount");
                if (amount == 0)
                    return One(inputs["image"]);
                return One(Run(Get("lens", Sources["lens"]), scope, u =>
                {
                    u["size"] = size;
                    u["amount"] = (float)amount;
                    u["fit"] = (float)(amount > 0 ? 1 + amount * 2 : 1);
                }, ("src", inputs["image"])));
            }
            case "mirror":
                return One(Run(Get("mirror", Sources["mirror"]), scope, u =>
                {
                    u["size"] = size;
                    u["axis"] = (float)c.Choice("axis");
                }, ("src", inputs["image"])));
            case "split-channels":
            {
                SKShader Channel(float index) => Run(Get("g.channel", Sources["g.channel"]), scope, u => u["index"] = index, ("src", inputs["image"]));
                return new() { ["r"] = Channel(0), ["g"] = Channel(1), ["b"] = Channel(2) };
            }
            case "merge-channels":
                return One(Run(Get("g.merge", Sources["g.merge"]), scope, _ => { },
                    ("r", inputs["r"]), ("g", inputs["g"]), ("b", inputs["b"])));
            case "color-matrix":
            {
                double brightness = c.Number("brightness"), contrast = c.Number("contrast"), saturation = c.Number("saturation");
                if (brightness == 0 && contrast == 0 && saturation == 0)
                    return One(inputs["image"]);
                var k = 1 + contrast;
                return One(Run(Get("g.matrix", Sources["g.matrix"]), scope, u =>
                {
                    u["k"] = (float)k;
                    u["add"] = (float)((128 * (1 - k) + brightness * 255) / 255.0);
                    u["sat"] = (float)(1 + saturation);
                }, ("src", inputs["image"])));
            }
            case "levels":
            {
                double black = c.Number("black"), white = c.Number("white"), gamma = c.Number("gamma");
                if (white <= black + 0.01)
                    white = black + 0.01;
                if (black == 0 && white == 1 && Math.Abs(gamma - 1) < 1e-6)
                    return One(inputs["image"]);
                return One(Run(Get("g.levels", Sources["g.levels"]), scope, u =>
                {
                    u["black"] = (float)black;
                    u["range"] = (float)(white - black);
                    u["invGamma"] = (float)(1 / gamma);
                }, ("src", inputs["image"])));
            }
            case "duotone":
            {
                var amount = c.Number("amount");
                if (amount <= 0)
                    return One(inputs["image"]);
                var low = Rgb(c.Color("shadows"));
                var high = Rgb(c.Color("highlights"));
                return One(Run(Get("duotone", Sources["duotone"]), scope, u =>
                {
                    u["amount"] = (float)amount;
                    u["low"] = low;
                    u["high"] = high;
                }, ("src", inputs["image"])));
            }
            default:
                return One(source);
        }
    }

    private static Dictionary<string, string> GraphSources() => new()
    {
        ["g.solid"] = """
            uniform float3 color;
            half4 main(float2 p) { return half4(half3(color), 1.0); }
            """,
        ["g.noise"] = HashFn + """

            uniform float cell; uniform float seed;
            half4 main(float2 p) {
                float n = hash3(floor(floor(p.x) / cell), floor(floor(p.y) / cell), seed);
                return half4(half3(n), 1.0);
            }
            """,
        ["g.mix"] = """
            uniform shader a; uniform shader b; uniform shader mask;
            uniform float amount; uniform float mode; uniform float useMask;
            half4 main(float2 p) {
                float3 va = float3(a.eval(p).rgb);
                float3 vb = float3(b.eval(p).rgb);
                float3 blended = vb;
                if (mode > 2.5) blended = 1.0 - (1.0 - va) * (1.0 - vb);
                else if (mode > 1.5) blended = va * vb;
                else if (mode > 0.5) blended = va + vb;
                float w = amount * (useMask > 0.5 ? float(mask.eval(p).r) : 1.0);
                return half4(half3(clamp(va + (blended - va) * w, 0.0, 1.0)), 1.0);
            }
            """,
        ["g.maskLuma"] = """
            uniform shader src; uniform float threshold; uniform float softness; uniform float invert;
            half4 main(float2 p) {
                float luma = dot(float3(src.eval(p).rgb), float3(0.2126, 0.7152, 0.0722));
                float t = clamp((luma - (threshold - softness * 0.5)) / softness, 0.0, 1.0);
                float v = invert > 0.5 ? 1.0 - t : t;
                return half4(half3(v), 1.0);
            }
            """,
        ["g.uv"] = """
            uniform shader src; uniform float2 size; uniform float2 offset; uniform float edge;
            float fold(float v) {
                if (edge < 0.5) return v;
                if (edge < 1.5) return v - floor(v);
                float m = mod(v, 2.0);
                return m > 1.0 ? 2.0 - m : m;
            }
            half4 main(float2 p) {
                float2 uv = p / size + offset;
                float2 q = float2(fold(uv.x), fold(uv.y)) * size;
                return src.eval(clamp(q, float2(0.5), size - 0.5));
            }
            """,
        ["g.channel"] = """
            uniform shader src; uniform float index;
            half4 main(float2 p) {
                half3 c = src.eval(p).rgb;
                half v = index < 0.5 ? c.r : (index < 1.5 ? c.g : c.b);
                return half4(half3(v), 1.0);
            }
            """,
        ["g.merge"] = """
            uniform shader r; uniform shader g; uniform shader b;
            half4 main(float2 p) { return half4(r.eval(p).r, g.eval(p).g, b.eval(p).b, 1.0); }
            """,
        ["g.matrix"] = """
            uniform shader src; uniform float k; uniform float add; uniform float sat;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float3 rgb = float3(c.rgb);
                float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                float3 o = (float3(luma) + (rgb - luma) * sat) * k + add;
                return half4(half3(clamp(o, 0.0, 1.0)), c.a);
            }
            """,
        ["g.levels"] = """
            uniform shader src; uniform float black; uniform float range; uniform float invGamma;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float3 v = clamp((float3(c.rgb) - black) / range, 0.0, 1.0);
                return half4(half3(pow(v, float3(invGamma))), c.a);
            }
            """,
    };
}
