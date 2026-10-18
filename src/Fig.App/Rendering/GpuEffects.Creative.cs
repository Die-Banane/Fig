using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// Shader ports of the creative library: the effects and transitions in <c>CreativeEffects.cs</c>,
/// <c>CreativeTransitions.cs</c> and the chromatic-aberration effect. Each one mirrors its CPU processor
/// (same maths, same clamping, same noise) so the preview, the GPU export and the CPU fallback agree.
/// Pixel coordinates in a shader are centers (x + 0.5); the CPU kernels use integer indices, hence the 0.5s.
/// </summary>
internal static partial class GpuEffects
{
    private const int MaxBlurRadius = 128;

    private static readonly HashSet<string> CreativeEffectIds =
    [
        "soft-blur", "directional-blur", "zoom-blur", "glow", "film-grain", "vhs", "digital-glitch", "camera-shake",
        "pulse-zoom", "strobe", "lens-distortion", "mirror", "light-leak", "duotone", EffectCatalog.ChromaticAberration,
    ];

    private static readonly HashSet<string> CreativeTransitionIds =
    [
        "white-flash", "blur-dissolve", "zoom-in", "zoom-out", "whip-pan", "spin", "glitch-cut",
        "pixel-dissolve", "luma-dissolve", "radial-wipe", "blinds", "card-flip",
    ];

    private static float[] Pair(double a, double b) => [(float)a, (float)b];

    private static readonly int[] BlurBounds = [4, 8, 16, 32, 64, MaxBlurRadius];

    /// <summary>
    /// One pass of the box blur along an axis. The loop bound has to be a constant, so there is one program per
    /// size class; a small radius then compiles (and, on a software canvas, unrolls) a short loop instead of 257 steps.
    /// </summary>
    private static string BlurSource(char axis, int bound)
    {
        var tap = axis == 'H'
            ? "float2(clamp(c + fk, 0.0, size.x - 1.0) + 0.5, p.y)"
            : "float2(p.x, clamp(c + fk, 0.0, size.y - 1.0) + 0.5)";
        var center = axis == 'H' ? "floor(p.x)" : "floor(p.y)";
        return $$"""
            uniform shader src; uniform float2 size; uniform float radius;
            half4 main(float2 p) {
                float c = {{center}};
                float4 sum = float4(0.0);
                for (int k = -{{bound}}; k <= {{bound}}; k++) {
                    float fk = float(k);
                    if (abs(fk) > radius) continue;
                    sum += float4(src.eval({{tap}}));
                }
                return half4(sum / (2.0 * radius + 1.0));
            }
            """;
    }

    /// <summary>A separable box blur: the horizontal pass is rendered once, the vertical pass stays lazy.</summary>
    private static SKShader BoxBlur(SKShader source, int radius, int w, int h, ShaderScope scope)
    {
        radius = Math.Clamp(radius, 1, MaxBlurRadius);
        var bound = BlurBounds.First(b => b >= radius);
        var size = Pair(w, h);
        var horizontal = scope.Materialize(
            Run(Get($"blurH{bound}", BlurSource('H', bound)), scope, u => { u["size"] = size; u["radius"] = (float)radius; }, ("src", source)),
            w, h);
        return Run(Get($"blurV{bound}", BlurSource('V', bound)), scope, u => { u["size"] = size; u["radius"] = (float)radius; }, ("src", horizontal));
    }

    private static SKShader? ApplyCreative(SKShader input, EffectCall call, int w, int h, ShaderScope scope)
    {
        var p = EffectCatalog.ParamsOf(call.TypeId, call.Params);
        var t = call.LocalT;
        var size = Pair(w, h);
        switch (call.TypeId)
        {
            case "soft-blur":
            {
                var amount = p.Number("amount");
                return amount == 0 ? input : BoxBlur(input, Math.Max(1, (int)Math.Round(amount * Math.Min(w, h))), w, h, scope);
            }
            case "directional-blur":
            {
                var distance = p.Number("amount") * w;
                if (distance == 0)
                    return input;
                var angle = p.Number("angle") * Math.PI / 180;
                var step = Pair(Math.Cos(angle) * distance / 4, Math.Sin(angle) * distance / 4);
                return Run(Get("dirBlur", Sources["dirBlur"]), scope, u => { u["size"] = size; u["stepv"] = step; }, ("src", input));
            }
            case "zoom-blur":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                return Run(Get("zoomBlur", Sources["zoomBlur"]), scope, u => { u["size"] = size; u["amount"] = (float)amount; }, ("src", input));
            }
            case "glow":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                var threshold = p.Number("threshold");
                var radius = Math.Max(1, (int)(p.Number("radius") * Math.Min(w, h)));
                var highlights = scope.Materialize(
                    Run(Get("glowHigh", Sources["glowHigh"]), scope, u => u["threshold"] = (float)threshold, ("src", input)), w, h);
                var halo = BoxBlur(highlights, radius, w, h, scope);
                return Run(Get("glowAdd", Sources["glowAdd"]), scope, u => u["amount"] = (float)amount, ("src", input), ("halo", halo));
            }
            case "film-grain":
            {
                var amount = p.Number("amount") * 100;
                if (amount == 0)
                    return input;
                var cell = (float)(int)p.Number("size");
                var seed = (float)Math.Floor(t * 24);
                return Run(Get("grain", Sources["grain"]), scope, u =>
                {
                    u["amount"] = (float)(amount / 255.0);
                    u["cell"] = cell;
                    u["seed"] = seed;
                }, ("src", input));
            }
            case "vhs":
            {
                var amount = p.Number("amount");
                var speed = p.Number("speed");
                if (amount == 0)
                    return input;
                var seed = (float)Math.Floor(t * 30);
                return Run(Get("vhs", Sources["vhs"]), scope, u =>
                {
                    u["size"] = size;
                    u["amount"] = (float)amount;
                    u["phase"] = (float)(t * speed * 8);
                    u["seed"] = seed;
                }, ("src", input));
            }
            case "digital-glitch":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                var seed = (float)Math.Floor(t * p.Number("speed"));
                return Run(Get("glitch", Sources["glitch"]), scope, u =>
                {
                    u["size"] = size;
                    u["amount"] = (float)amount;
                    u["seed"] = seed;
                }, ("src", input));
            }
            case "camera-shake":
            {
                var amount = p.Number("amount");
                var speed = p.Number("speed");
                if (amount == 0)
                    return input;
                var dx = amount * (Math.Sin(t * speed * 6.28) + 0.4 * Math.Sin(t * speed * 13.7)) / 1.4;
                var dy = amount * (Math.Sin(t * speed * 8.1 + 1) + 0.4 * Math.Sin(t * speed * 17.3)) / 1.4;
                return Zoomed(input, 1 + amount * 5, dx, dy, size, scope);
            }
            case "pulse-zoom":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                var pulse = Math.Pow(0.5 + 0.5 * Math.Cos(t * p.Number("bpm") / 60 * Math.PI * 2), 4);
                return Zoomed(input, 1 + amount * pulse, 0, 0, size, scope);
            }
            case "strobe":
            {
                var phase = t * p.Number("frequency");
                var flash = Math.Max(0, 1 - (phase - Math.Floor(phase)) / 0.2) * p.Number("amount");
                if (flash <= 0)
                    return input;
                var dark = p.Bool("dark");
                return Run(Get("strobe", Sources["strobe"]), scope, u =>
                {
                    u["flash"] = (float)flash;
                    u["target"] = dark ? 0f : 1f;
                }, ("src", input));
            }
            case "lens-distortion":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                return Run(Get("lens", Sources["lens"]), scope, u =>
                {
                    u["size"] = size;
                    u["amount"] = (float)amount;
                    u["fit"] = (float)(amount > 0 ? 1 + amount * 2 : 1);
                }, ("src", input));
            }
            case "mirror":
                return Run(Get("mirror", Sources["mirror"]), scope, u =>
                {
                    u["size"] = size;
                    u["axis"] = (float)p.Choice("axis");
                }, ("src", input));
            case "light-leak":
            {
                var amount = p.Number("amount");
                var speed = p.Number("speed");
                if (amount == 0)
                    return input;
                return Run(Get("leak", Sources["leak"]), scope, u =>
                {
                    u["size"] = size;
                    u["amount"] = (float)amount;
                    u["center"] = (float)(-0.12 + Math.Sin(t * speed) * 0.15);
                }, ("src", input));
            }
            case "duotone":
            {
                var amount = p.Number("amount");
                if (amount == 0)
                    return input;
                var low = Rgb(p.Color("shadows"));
                var high = Rgb(p.Color("highlights"));
                return Run(Get("duotone", Sources["duotone"]), scope, u =>
                {
                    u["amount"] = (float)amount;
                    u["low"] = low;
                    u["high"] = high;
                }, ("src", input));
            }
            case EffectCatalog.ChromaticAberration:
                return Chromatic(input, p, w, h, scope);
            default:
                return null;
        }
    }

    /// <summary>Zoom about the center plus an offset (fractions of the frame), as the CPU Warp-based effects do.</summary>
    private static SKShader Zoomed(SKShader input, double zoom, double dx, double dy, float[] size, ShaderScope scope)
        => Run(Get("zoomShift", Sources["zoomShift"]), scope, u =>
        {
            u["size"] = size;
            u["zoom"] = (float)zoom;
            u["shiftv"] = Pair(dx, dy);
        }, ("src", input));

    private static SKShader Chromatic(SKShader input, ParamSet p, int w, int h, ShaderScope scope)
    {
        var radial = p.Bool("radial");
        var xOffset = p.Number("x_offset");
        var yOffset = p.Number("y_offset");
        if (Math.Abs(xOffset) < 1e-6 && Math.Abs(yOffset) < 1e-6)
            return input;
        // both axes are scaled by the width, like the CPU effect
        double dx = xOffset * w, dy = yOffset * w;
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        return Run(Get("chroma", Sources["chroma"]), scope, u =>
        {
            u["size"] = Pair(w, h);
            u["radial"] = radial ? 1f : 0f;
            u["fixedShift"] = Pair(Math.Round(dx), Math.Round(dy));
            u["radialShift"] = Pair(dx, dy);
            u["center"] = Pair(cx, cy);
            u["maxDist"] = (float)Math.Max(1.0, Math.Sqrt(cx * cx + cy * cy));
        }, ("src", input));
    }

    // ------------------------------------------------------------------ transitions

    private static SKShader? CreativeTransition(SKShader a, SKShader b, string typeId, double t01,
        ParamSet p, int w, int h, ShaderScope scope)
    {
        if (!CreativeTransitionIds.Contains(typeId))
            return null;
        var t = Math.Clamp(t01, 0, 1);
        var size = Pair(w, h);
        var progress = t * t * (3 - 2 * t);
        var peak = Math.Sin(Math.PI * t);
        // the kinds share this code but not their parameters, as on the CPU
        var amount = p.Declares("amount") ? p.Number("amount") : 0;
        var softness = p.Declares("softness") ? p.Number("softness") : 0.08;
        var direction = p.Declares("direction") && p.Choice("direction") == 1 ? -1.0 : 1.0;

        void Pair2(SKRuntimeEffectUniforms u)
        {
            u["size"] = size;
            u["progress"] = (float)progress;
        }

        switch (typeId)
        {
            case "white-flash":
                return Run(Get("tx.flash", Sources["tx.flash"]), scope, u =>
                {
                    Pair2(u);
                    u["flash"] = (float)(Math.Pow(peak, 6) * amount);
                }, ("a", a), ("b", b));
            case "blur-dissolve":
            {
                var mixed = Run(Get("tx.mix", Sources["tx.mix"]), scope, Pair2, ("a", a), ("b", b));
                var radius = (int)Math.Round(peak * amount * Math.Min(w, h) * 0.08);
                return radius == 0 ? mixed : BoxBlur(mixed, radius, w, h, scope);
            }
            case "zoom-in":
            case "zoom-out":
                return Run(Get("tx.zoom", Sources["tx.zoom"]), scope, u =>
                {
                    Pair2(u);
                    u["k"] = (float)((typeId == "zoom-in" ? 1 : -1) * amount * 1.5);
                }, ("a", a), ("b", b));
            case "whip-pan":
                return Run(Get("tx.whip", Sources["tx.whip"]), scope, u =>
                {
                    Pair2(u);
                    u["dirv"] = (float)direction;
                }, ("a", a), ("b", b));
            case "spin":
                return Run(Get("tx.spin", Sources["tx.spin"]), scope, u =>
                {
                    Pair2(u);
                    u["turn"] = (float)(direction * amount * Math.PI);
                    u["zoom"] = (float)(1 + peak * amount);
                }, ("a", a), ("b", b));
            case "glitch-cut":
                return Run(Get("tx.glitch", Sources["tx.glitch"]), scope, u =>
                {
                    Pair2(u);
                    u["shift"] = (float)(peak * amount * 0.35);
                    u["seed"] = (float)(int)(t * 24);
                }, ("a", a), ("b", b));
            case "card-flip":
                return Run(Get("tx.flip", Sources["tx.flip"]), scope, u =>
                {
                    u["size"] = size;
                    u["t"] = (float)t;
                    u["width"] = (float)Math.Max(0.001, Math.Abs(Math.Cos(Math.PI * t)));
                }, ("a", a), ("b", b));
            default:
            {
                // pixel / luma / radial / blinds: a per-pixel reveal threshold
                var kind = typeId switch { "pixel-dissolve" => 0f, "luma-dissolve" => 1f, "radial-wipe" => 2f, _ => 3f };
                var columns = p.Declares("tiles") ? p.Int("tiles") : 1;
                var rows = Math.Max(1, (int)Math.Round(columns * (double)h / w));
                return Run(Get("tx.reveal", Sources["tx.reveal"]), scope, u =>
                {
                    Pair2(u);
                    u["kind"] = kind;
                    u["soft"] = (float)softness;
                    u["reveal"] = (float)(progress * (1 + softness) - softness / 2);
                    u["tiles"] = Pair(columns, rows);
                    u["stripes"] = p.Declares("stripes") ? (float)p.Number("stripes") : 1f;
                    u["dirv"] = (float)direction;
                }, ("a", a), ("b", b));
            }
        }
    }

    // ------------------------------------------------------------------ SkSL

    /// <summary>Same formula as <c>CreativeFrameOps.Noise</c> on the CPU.</summary>
    private const string HashFn = """
        float hash3(float x, float y, float s) {
            float3 p = fract(float3(x, y, s) * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return fract((p.x + p.y) * p.z);
        }
        """;

    private static Dictionary<string, string> CreativeSources() => new()
    {
        ["dirBlur"] = """
            uniform shader src; uniform float2 size; uniform float2 stepv;
            half4 main(float2 p) {
                float4 sum = float4(0.0);
                for (int k = -4; k <= 4; k++) {
                    float2 q = clamp(p + stepv * float(k), float2(0.5), size - 0.5);
                    sum += float4(src.eval(q));
                }
                return half4(sum / 9.0);
            }
            """,
        ["zoomBlur"] = """
            uniform shader src; uniform float2 size; uniform float amount;
            half4 main(float2 p) {
                float2 c = size * 0.5;
                float4 sum = float4(0.0);
                for (int k = 0; k < 10; k++) {
                    float s = 1.0 - amount * float(k) / 9.0;
                    float2 q = clamp(c + (p - c) * s, float2(0.5), size - 0.5);
                    sum += float4(src.eval(q));
                }
                return half4(sum / 10.0);
            }
            """,
        ["glowHigh"] = """
            uniform shader src; uniform float threshold;
            half4 main(float2 p) {
                half3 c = src.eval(p).rgb;
                return half4(max(c - half(threshold), half3(0.0)), 1.0);
            }
            """,
        ["glowAdd"] = """
            uniform shader src; uniform shader halo; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half3 h = halo.eval(p).rgb;
                return half4(clamp(c.rgb + h * half(amount), 0.0, 1.0), c.a);
            }
            """,
        ["grain"] = HashFn + """

            uniform shader src; uniform float amount; uniform float cell; uniform float seed;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float n = hash3(floor(floor(p.x) / cell), floor(floor(p.y) / cell), seed);
                c.rgb = clamp(c.rgb + half((n - 0.5) * amount), 0.0, 1.0);
                return c;
            }
            """,
        ["vhs"] = HashFn + """

            uniform shader src; uniform float2 size; uniform float amount; uniform float phase; uniform float seed;
            half4 main(float2 p) {
                float v = floor(p.y) / size.y;
                float shiftx = sin(v * 35.0 + phase) * size.x * amount * 0.006;
                float scan = 1.0 - amount * 0.18 * (0.5 + 0.5 * cos(v * 360.0 * 3.14159265358979));
                float noise = (hash3(floor(p.x), floor(p.y), seed) - 0.5) * amount * 30.0 / 255.0;
                float bleed = size.x * amount * 0.004;
                float y = p.y;
                float r = src.eval(float2(clamp(p.x + shiftx + bleed, 0.5, size.x - 0.5), y)).r;
                float g = src.eval(float2(clamp(p.x + shiftx, 0.5, size.x - 0.5), y)).g;
                float b = src.eval(float2(clamp(p.x + shiftx - bleed, 0.5, size.x - 0.5), y)).b;
                float a = src.eval(p).a;
                return half4(half3(clamp(float3(r, g, b) * scan + noise * a, 0.0, a)), half(a));
            }
            """,
        ["glitch"] = HashFn + """

            uniform shader src; uniform float2 size; uniform float amount; uniform float seed;
            half4 main(float2 p) {
                float band = floor(floor(p.y) * 24.0 / size.y);
                float shiftx = hash3(0.0, band, seed) > 0.65 ? (hash3(1.0, band, seed) - 0.5) * size.x * amount * 0.25 : 0.0;
                float split = size.x * amount * 0.015;
                float r = src.eval(float2(clamp(p.x + shiftx + split, 0.5, size.x - 0.5), p.y)).r;
                float g = src.eval(float2(clamp(p.x + shiftx, 0.5, size.x - 0.5), p.y)).g;
                float b = src.eval(float2(clamp(p.x + shiftx - split, 0.5, size.x - 0.5), p.y)).b;
                return half4(half3(float3(r, g, b)), src.eval(p).a);
            }
            """,
        ["zoomShift"] = """
            uniform shader src; uniform float2 size; uniform float zoom; uniform float2 shiftv;
            half4 main(float2 p) {
                float2 uv = p / size;
                float2 q = (0.5 + (uv - 0.5) / zoom + shiftv) * size;
                return src.eval(clamp(q, float2(0.5), size - 0.5));
            }
            """,
        ["strobe"] = """
            uniform shader src; uniform float flash; uniform float target;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                c.rgb = half3(mix(float3(c.rgb), float3(target), flash));
                return c;
            }
            """,
        ["lens"] = """
            uniform shader src; uniform float2 size; uniform float amount; uniform float fit;
            half4 main(float2 p) {
                float2 d = p / size - 0.5;
                float k = max(0.0, 1.0 + amount * dot(d, d) * 4.0) / fit;
                float2 q = (0.5 + d * k) * size;
                return src.eval(clamp(q, float2(0.5), size - 0.5));
            }
            """,
        ["mirror"] = """
            uniform shader src; uniform float2 size; uniform float axis;
            half4 main(float2 p) {
                float2 uv = p / size;
                float u = axis != 1.0 ? min(uv.x, 1.0 - uv.x) : uv.x;
                float v = axis != 0.0 ? min(uv.y, 1.0 - uv.y) : uv.y;
                return src.eval(clamp(float2(u, v) * size, float2(0.5), size - 0.5));
            }
            """,
        ["leak"] = """
            uniform shader src; uniform float2 size; uniform float amount; uniform float center;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float u = p.x / size.x, v = p.y / size.y;
                float a = (u - center) / 0.3, b = (v - 0.4) / 0.8;
                float light = exp(-a * a - b * b) * amount;
                float3 tint = float3(255.0, 110.0, 35.0) / 255.0;
                c.rgb = half3(clamp(1.0 - (1.0 - float3(c.rgb)) * (1.0 - light * tint), 0.0, 1.0));
                return c;
            }
            """,
        ["duotone"] = """
            uniform shader src; uniform float amount; uniform float3 low; uniform float3 high;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float luma = dot(float3(c.rgb), float3(0.2126, 0.7152, 0.0722));
                float3 mapped = low * (1.0 - luma) + high * luma;
                c.rgb = half3(clamp(mix(float3(c.rgb), mapped, amount), 0.0, 1.0));
                return c;
            }
            """,
        ["chroma"] = """
            uniform shader src; uniform float2 size; uniform float radial;
            uniform float2 fixedShift; uniform float2 radialShift; uniform float2 center; uniform float maxDist;
            half4 main(float2 p) {
                float2 px = floor(p);
                float2 o = fixedShift;
                if (radial > 0.5) o = radialShift * (px - center) / maxDist;
                float2 rq = clamp(floor(px - o + 0.5), float2(0.0), size - 1.0) + 0.5;
                float2 bq = clamp(floor(px + o + 0.5), float2(0.0), size - 1.0) + 0.5;
                half4 c = src.eval(p);
                return half4(src.eval(rq).r, c.g, src.eval(bq).b, c.a);
            }
            """,

        // transitions: size, progress (smoothstepped), plus per-kind uniforms
        ["tx.flash"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float progress; uniform float flash;
            half4 main(float2 p) {
                half4 m = mix(a.eval(p), b.eval(p), half(progress));
                m.rgb = half3(mix(float3(m.rgb), float3(1.0), flash));
                return m;
            }
            """,
        ["tx.mix"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float progress;
            half4 main(float2 p) { return mix(a.eval(p), b.eval(p), half(progress)); }
            """,
        ["tx.zoom"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float progress; uniform float k;
            // the zoomed-out side is smaller than the frame: mirror it out to the edges, as the CPU does
            float2 mirror(float2 t) { return 1.0 - abs(1.0 - mod(t, 2.0)); }
            half4 main(float2 p) {
                float2 uv = p / size - 0.5;
                float2 qa = mirror(0.5 + uv * exp(k * -progress)) * size;
                float2 qb = mirror(0.5 + uv * exp(k * (1.0 - progress))) * size;
                half4 ca = a.eval(clamp(qa, float2(0.5), size - 0.5));
                half4 cb = b.eval(clamp(qb, float2(0.5), size - 0.5));
                return mix(ca, cb, half(progress));
            }
            """,
        ["tx.whip"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float progress; uniform float dirv;
            half4 main(float2 p) {
                float u = p.x / size.x;
                float ua = u + dirv * -progress;
                float ub = u + dirv * (1.0 - progress);
                ua = ua - floor(ua);
                ub = ub - floor(ub);
                half4 ca = a.eval(float2(clamp(ua * size.x, 0.5, size.x - 0.5), p.y));
                half4 cb = b.eval(float2(clamp(ub * size.x, 0.5, size.x - 0.5), p.y));
                return mix(ca, cb, half(progress));
            }
            """,
        ["tx.spin"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float progress;
            uniform float turn; uniform float zoom;
            float2 rot(float2 uv, float angle) {
                float aspect = size.x / size.y;
                float x = (uv.x - 0.5) * aspect, y = uv.y - 0.5;
                float c = cos(angle), s = sin(angle);
                return float2(0.5 + (x * c - y * s) / aspect / zoom, 0.5 + (x * s + y * c) / zoom);
            }
            half4 main(float2 p) {
                float2 uv = p / size;
                float2 qa = rot(uv, turn * -progress) * size;
                float2 qb = rot(uv, turn * (1.0 - progress)) * size;
                half4 ca = a.eval(clamp(qa, float2(0.5), size - 0.5));
                half4 cb = b.eval(clamp(qb, float2(0.5), size - 0.5));
                return mix(ca, cb, half(progress));
            }
            """,
        ["tx.glitch"] = HashFn + """

            uniform shader a; uniform shader b; uniform float2 size; uniform float progress;
            uniform float shift; uniform float seed;
            half4 main(float2 p) {
                float band = floor(p.y / size.y * 24.0);
                float shiftx = (hash3(0.0, band, seed) - 0.5) * shift;
                float2 q = float2(clamp((p.x / size.x + shiftx) * size.x, 0.5, size.x - 0.5), p.y);
                return mix(a.eval(q), b.eval(q), half(progress));
            }
            """,
        ["tx.flip"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float width;
            half4 main(float2 p) {
                float u = p.x / size.x;
                if (abs(u - 0.5) > width / 2.0) return half4(0.0, 0.0, 0.0, 1.0);
                float2 q = float2(clamp((0.5 + (u - 0.5) / width) * size.x, 0.5, size.x - 0.5), p.y);
                return t < 0.5 ? a.eval(q) : b.eval(q);
            }
            """,
        ["tx.reveal"] = HashFn + """

            uniform shader a; uniform shader b; uniform float2 size; uniform float progress; uniform float kind;
            uniform float soft; uniform float reveal; uniform float2 tiles; uniform float stripes; uniform float dirv;
            half4 main(float2 p) {
                float u = p.x / size.x, v = p.y / size.y;
                half4 ca = a.eval(p);
                float key;
                if (kind < 0.5) {
                    key = hash3(floor(u * tiles.x), floor(v * tiles.y), 17.0);
                } else if (kind < 1.5) {
                    float luma = dot(float3(ca.rgb), float3(0.2126, 0.7152, 0.0722));
                    key = 1.0 - luma;
                } else if (kind < 2.5) {
                    float angle = mod(atan(v - 0.5, (u - 0.5) * size.x / size.y) / 6.28318530717959 + 1.25, 1.0);
                    key = dirv < 0.0 ? 1.0 - angle : angle;
                } else {
                    float pos = u * stripes;
                    key = pos - floor(pos);
                }
                float w = clamp(0.5 + (reveal - key) / soft, 0.0, 1.0);
                return mix(ca, b.eval(p), half(w));
            }
            """,
    };
}
