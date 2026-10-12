using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SkiaSharp;

namespace Fig.App.Rendering;

/// <summary>
/// The effect and transition kernels as SkSL runtime shaders, ported one-to-one from the CPU
/// processors in Fig.Core so the GPU preview matches what export renders. Each effect wraps an input
/// shader; coordinates are canvas pixels (the renderer scales the canvas to the widget).
/// </summary>
internal static class GpuEffects
{
    private static readonly ConcurrentDictionary<string, SKRuntimeEffect> Compiled = new();

    private static SKRuntimeEffect Get(string key, string sksl) => Compiled.GetOrAdd(key, _ =>
    {
        var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        return effect ?? throw new InvalidOperationException($"SkSL '{key}' failed to compile: {errors}");
    });

    /// <summary>Pre-compiles every shader, surfacing SkSL errors at startup or in tests rather than mid-playback.</summary>
    public static void CompileAll()
    {
        foreach (var (key, source) in Sources)
            Get(key, source);
    }

    private static readonly HashSet<string> GpuEffectIds =
    [
        EffectCatalog.Brightness, EffectCatalog.Contrast, EffectCatalog.Grayscale, EffectCatalog.Saturation,
        EffectCatalog.Sepia, EffectCatalog.Invert, EffectCatalog.Hue, EffectCatalog.Tint, EffectCatalog.Vignette,
        EffectCatalog.Posterize, EffectCatalog.Pixelate, EffectCatalog.Flip, EffectCatalog.Sharpen,
        EffectCatalog.CropWithPadding, EffectCatalog.Transform,
    ];

    /// <summary>
    /// True when the effect has a shader. Effects without one are not dropped: <see cref="GpuSceneBuilder"/>
    /// runs such a clip's stack on the CPU before upload, so a new effect works in the preview immediately.
    /// </summary>
    public static bool Supports(string typeId) => GpuEffectIds.Contains(typeId);

    // ---- parameter helpers (the CPU processors clamp the same way) ----

    private static double Num(IReadOnlyDictionary<string, ParamValue> p, string key, double fallback)
        => p.TryGetValue(key, out var v) && v.Kind is ParamKind.Double or ParamKind.Int ? v.AsNumber : fallback;

    private static int Choice(IReadOnlyDictionary<string, ParamValue> p, string key, int fallback)
        => p.TryGetValue(key, out var v) && v.Kind == ParamKind.List ? v.AsChoice : fallback;

    private static uint ColorOf(IReadOnlyDictionary<string, ParamValue> p, string key, uint fallback)
        => p.TryGetValue(key, out var v) && v.Kind == ParamKind.Color ? v.AsColor : fallback;

    private static float[] Rgb(uint argb) =>
        [((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f];

    private static SKShader Run(SKRuntimeEffect effect, ShaderScope scope, Action<SKRuntimeEffectUniforms> uniforms,
        params (string Name, SKShader Shader)[] children)
    {
        var u = new SKRuntimeEffectUniforms(effect);
        uniforms(u);
        var c = new SKRuntimeEffectChildren(effect);
        foreach (var (name, shader) in children)
            c[name] = shader;
        return scope.Add(effect.ToShader(u, c));
    }

    /// <summary>
    /// Forces alpha to 1. Decoded frames carry alpha 0 in their letterbox bars, but the CPU compositor
    /// ignores per-pixel alpha (the bars are plain black), so the GPU must too.
    /// </summary>
    public static SKShader Opaque(SKShader input, ShaderScope scope)
        => Run(Get("opaque", Sources["opaque"]), scope, _ => { }, ("src", input));

    /// <summary>Applies one effect to a shader. Returns the input unchanged when the effect is a no-op.</summary>
    public static SKShader Apply(SKShader input, GpuEffectCall call, int width, int height, ShaderScope scope)
    {
        var p = call.Params;
        var size = new[] { (float)width, (float)height };
        switch (call.TypeId)
        {
            case EffectCatalog.Brightness:
            {
                var amount = Math.Clamp(Num(p, "amount", 0), -1, 1);
                if (Math.Abs(amount) < 1e-6)
                    return input;
                var delta = (float)(Math.Round(amount * 255) / 255.0);
                return Run(Get("brightness", Sources["brightness"]), scope, u => u["delta"] = delta, ("src", input));
            }
            case EffectCatalog.Contrast:
            {
                var amount = Math.Clamp(Num(p, "amount", 1), 0, 2);
                if (Math.Abs(amount - 1) < 1e-6)
                    return input;
                return Run(Get("contrast", Sources["contrast"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.Grayscale:
            {
                var amount = Math.Clamp(Num(p, "amount", 1), 0, 1);
                if (amount < 1e-6)
                    return input;
                return Run(Get("grayscale", Sources["grayscale"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.Saturation:
            {
                var amount = Math.Clamp(Num(p, "amount", 1), 0, 2);
                if (Math.Abs(amount - 1) < 1e-6)
                    return input;
                return Run(Get("saturation", Sources["saturation"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.Sepia:
            {
                var amount = Math.Clamp(Num(p, "amount", 1), 0, 1);
                if (amount < 1e-6)
                    return input;
                return Run(Get("sepia", Sources["sepia"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.Invert:
            {
                var amount = Math.Clamp(Num(p, "amount", 1), 0, 1);
                if (amount < 1e-6)
                    return input;
                return Run(Get("invert", Sources["invert"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.Hue:
            {
                var degrees = ((Num(p, "degrees", 0) % 360) + 360) % 360;
                if (degrees < 1e-6 || degrees > 359.999)
                    return input;
                var rad = degrees * Math.PI / 180.0;
                var cos = Math.Cos(rad);
                var sin = Math.Sin(rad);
                float[] r0 = [(float)(0.213 + cos * 0.787 - sin * 0.213), (float)(0.715 - cos * 0.715 - sin * 0.715), (float)(0.072 - cos * 0.072 + sin * 0.928)];
                float[] r1 = [(float)(0.213 - cos * 0.213 + sin * 0.143), (float)(0.715 + cos * 0.285 + sin * 0.140), (float)(0.072 - cos * 0.072 - sin * 0.283)];
                float[] r2 = [(float)(0.213 - cos * 0.213 - sin * 0.787), (float)(0.715 - cos * 0.715 + sin * 0.715), (float)(0.072 + cos * 0.928 + sin * 0.072)];
                return Run(Get("hue", Sources["hue"]), scope, u =>
                {
                    u["r0"] = r0;
                    u["r1"] = r1;
                    u["r2"] = r2;
                }, ("src", input));
            }
            case EffectCatalog.Tint:
            {
                var strength = Math.Clamp(Num(p, "strength", 0), 0, 1);
                if (strength < 1e-6)
                    return input;
                var tint = Rgb(ColorOf(p, "color", 0xFF0080FFu));
                var preserve = p.TryGetValue("preserve_luma", out var pl) && pl.Kind == ParamKind.Bool && pl.AsBool;
                return Run(Get("tint", Sources["tint"]), scope, u =>
                {
                    u["tint"] = tint;
                    u["strength"] = (float)strength;
                    u["preserve"] = preserve ? 1f : 0f;
                }, ("src", input));
            }
            case EffectCatalog.Vignette:
            {
                var strength = Math.Clamp(Num(p, "strength", 0.35), 0, 1);
                if (strength < 1e-6)
                    return input;
                var color = Rgb(ColorOf(p, "color", 0xFF000000u));
                return Run(Get("vignette", Sources["vignette"]), scope, u =>
                {
                    u["size"] = size;
                    u["strength"] = (float)strength;
                    u["vcolor"] = color;
                }, ("src", input));
            }
            case EffectCatalog.Posterize:
            {
                var levels = Math.Max(2, (int)Math.Round(Num(p, "levels", 4)));
                var step = (float)(255.0 / (levels - 1));
                return Run(Get("posterize", Sources["posterize"]), scope, u => u["levelStep"] = step, ("src", input));
            }
            case EffectCatalog.Pixelate:
            {
                var block = Math.Max(1, (int)Math.Round(Num(p, "block", 8)));
                if (block == 1)
                    return input;
                return Run(Get("pixelate", Sources["pixelate"]), scope, u => u["block"] = (float)block, ("src", input));
            }
            case EffectCatalog.Flip:
            {
                var axis = Choice(p, "axis", 0);
                var flipX = axis is 0 or 2;
                var flipY = axis is 1 or 2;
                if (!flipX && !flipY)
                    return input;
                return Run(Get("flip", Sources["flip"]), scope, u =>
                {
                    u["size"] = size;
                    u["flipX"] = flipX ? 1f : 0f;
                    u["flipY"] = flipY ? 1f : 0f;
                }, ("src", input));
            }
            case EffectCatalog.Sharpen:
            {
                var amount = Math.Clamp(Num(p, "amount", 0.5), 0, 2);
                if (amount < 1e-6)
                    return input;
                return Run(Get("sharpen", Sources["sharpen"]), scope, u => u["amount"] = (float)amount, ("src", input));
            }
            case EffectCatalog.CropWithPadding:
                return CropWithPadding(input, p, width, height, scope);
            case EffectCatalog.Transform:
                return Transform(input, p, width, height, scope);
            default:
                return input;   // unknown effects are skipped, like the CPU pipeline
        }
    }

    private static SKShader CropWithPadding(SKShader input, IReadOnlyDictionary<string, ParamValue> p, int w, int h, ShaderScope scope)
    {
        var left = Math.Clamp(Num(p, "left", 0), 0, 0.5);
        var right = Math.Clamp(Num(p, "right", 0), 0, 0.5);
        var top = Math.Clamp(Num(p, "top", 0), 0, 0.5);
        var bottom = Math.Clamp(Num(p, "bottom", 0), 0, 0.5);
        var pad = Rgb(ColorOf(p, "color", 0xFF000000u));

        // visual coordinates: y counts up from the bottom edge, exactly like the CPU processor
        var x0 = (int)Math.Round(w * left);
        var x1 = Math.Max(x0, w - (int)Math.Round(w * right));
        var y0 = (int)Math.Round(h * bottom);
        var y1 = Math.Max(y0, h - (int)Math.Round(h * top));
        var cw = x1 - x0;
        var ch = y1 - y0;
        if (cw >= w && ch >= h)
            return input;

        var empty = cw <= 0 || ch <= 0;
        var scale = empty ? 1.0 : Math.Min(w / (double)cw, h / (double)ch);
        var contentW = cw * scale;
        var contentH = ch * scale;
        return Run(Get("cropPad", Sources["cropPad"]), scope, u =>
        {
            u["size"] = new[] { (float)w, (float)h };
            u["src0"] = new[] { (float)x0, (float)y0 };
            u["off"] = new[] { (float)((w - contentW) / 2.0), (float)((h - contentH) / 2.0) };
            u["content"] = new[] { (float)contentW, (float)contentH };
            u["scale"] = (float)scale;
            u["pad"] = pad;
            u["empty"] = empty ? 1f : 0f;
        }, ("src", input));
    }

    private static SKShader Transform(SKShader input, IReadOnlyDictionary<string, ParamValue> p, int w, int h, ShaderScope scope)
    {
        var view = TransformMath.ViewFrom(p);
        if (view.IsIdentity)
            return input;
        var radians = view.RotationDegrees * Math.PI / 180.0;
        return Run(Get("transform", Sources["transform"]), scope, u =>
        {
            u["size"] = new[] { (float)w, (float)h };
            u["center"] = new[] { (float)(view.CenterX * w), (float)(view.CenterY * h) };
            u["cs"] = new[] { (float)Math.Cos(radians), (float)Math.Sin(radians) };
            u["invZoom"] = (float)(1.0 / view.Zoom);
        }, ("src", input));
    }

    /// <summary>
    /// Crops to the given insets, scaling the kept region back up to the full canvas with the same integer
    /// nearest-neighbour mapping as the CPU compositor, so preview and export agree pixel for pixel.
    /// </summary>
    public static SKShader Crop(SKShader input, CropInsets crop, int width, int height, ShaderScope scope)
    {
        var x = (int)Math.Round(crop.Left * width);
        var y = (int)Math.Round(crop.Top * height);
        var w = (int)Math.Round((1 - crop.Left - crop.Right) * width);
        var h = (int)Math.Round((1 - crop.Top - crop.Bottom) * height);
        if (w < 1 || h < 1 || (x <= 0 && y <= 0 && w >= width && h >= height))
            return input;
        var cx = Math.Clamp(x, 0, Math.Max(0, width - 1));
        var cy = Math.Clamp(y, 0, Math.Max(0, height - 1));
        var cw = Math.Clamp(w, 1, width - cx);
        var ch = Math.Clamp(h, 1, height - cy);
        return Run(Get("crop", Sources["crop"]), scope, u =>
        {
            u["size"] = new[] { (float)width, (float)height };
            u["origin"] = new[] { (float)cx, (float)cy };
            u["span"] = new[] { (float)cw, (float)ch };
        }, ("src", input));
    }

    /// <summary>Blends two shaders with a catalog transition at progress <paramref name="t01"/>.</summary>
    public static SKShader Transition(SKShader a, SKShader b, string typeId, double t01,
        IReadOnlyDictionary<string, ParamValue> p, int width, int height, ShaderScope scope)
    {
        t01 = Math.Clamp(t01, 0, 1);
        var size = new[] { (float)width, (float)height };
        void Common(SKRuntimeEffectUniforms u)
        {
            u["size"] = size;
            u["t"] = (float)t01;
        }

        switch (typeId)
        {
            case TransitionCatalog.CrossDissolve:
                return Run(Get("tx.dissolve", Sources["tx.dissolve"]), scope, Common, ("a", a), ("b", b));
            case TransitionCatalog.FadeToBlack:
                return Run(Get("tx.fade", Sources["tx.fade"]), scope, Common, ("a", a), ("b", b));
            case TransitionCatalog.Wipe:
            {
                var soft = (float)Math.Clamp(Num(p, "soft", 0.1), 0, 0.5);
                return Run(Get("tx.wipe", Sources["tx.wipe"]), scope, u => { Common(u); u["soft"] = soft; }, ("a", a), ("b", b));
            }
            case TransitionCatalog.Curtain:
            {
                var panels = (float)Math.Max(2, (int)Math.Round(Num(p, "panels", 6)));
                var soft = (float)Math.Clamp(Num(p, "soft", 0.1), 0, 0.5);
                return Run(Get("tx.curtain", Sources["tx.curtain"]), scope,
                    u => { Common(u); u["panels"] = panels; u["soft"] = soft; }, ("a", a), ("b", b));
            }
            case TransitionCatalog.Iris:
            {
                var soft = (float)Math.Clamp(Num(p, "soft", 0.1), 0, 0.5);
                return Run(Get("tx.iris", Sources["tx.iris"]), scope, u => { Common(u); u["soft"] = soft; }, ("a", a), ("b", b));
            }
            case TransitionCatalog.Push:
            {
                var dir = (float)Choice(p, "direction", 0);
                return Run(Get("tx.push", Sources["tx.push"]), scope, u => { Common(u); u["dir"] = dir; }, ("a", a), ("b", b));
            }
            case TransitionCatalog.Slide:
            {
                var dir = (float)Choice(p, "direction", 0);
                return Run(Get("tx.slide", Sources["tx.slide"]), scope, u => { Common(u); u["dir"] = dir; }, ("a", a), ("b", b));
            }
            case TransitionCatalog.Swirl:
            {
                var center = new[]
                {
                    (float)(Math.Clamp(Num(p, "centerX", 0.5), 0, 1) * width),
                    (float)(Math.Clamp(Num(p, "centerY", 0.5), 0, 1) * height),
                };
                var radius = (float)(Math.Clamp(Num(p, "radius", 0.42), 0.05, 1) * Math.Min(width, height));
                var rotation = (float)(Num(p, "rotation", 90) * Math.PI / 180.0);
                var direction = Choice(p, "direction", 0) == 1 ? -1f : 1f;
                var easing = (float)Choice(p, "easing", 3);
                return Run(Get("tx.swirl", Sources["tx.swirl"]), scope, u =>
                {
                    Common(u);
                    u["center"] = center;
                    u["radius"] = radius;
                    u["rotation"] = rotation;
                    u["direction"] = direction;
                    u["easing"] = easing;
                }, ("a", a), ("b", b));
            }
            default:
                // unknown transition: hard cut at the midpoint, like the CPU path
                return t01 < 0.5 ? a : b;
        }
    }

    // ---------------------------------------------------------------- SkSL sources

    private static readonly Dictionary<string, string> Sources = new()
    {
        ["opaque"] = """
            uniform shader src;
            half4 main(float2 p) { return half4(src.eval(p).rgb, 1.0); }
            """,
        ["brightness"] = """
            uniform shader src; uniform float delta;
            half4 main(float2 p) { half4 c = src.eval(p); c.rgb = clamp(c.rgb + half(delta), 0.0, 1.0); return c; }
            """,
        ["contrast"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                const half mid = half(128.0 / 255.0);
                c.rgb = clamp((c.rgb - mid) * half(amount) + mid, 0.0, 1.0);
                return c;
            }
            """,
        ["grayscale"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half l = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = mix(c.rgb, half3(l), half(amount));
                return c;
            }
            """,
        ["saturation"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half l = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = clamp(half3(l) + (c.rgb - half3(l)) * half(amount), 0.0, 1.0);
                return c;
            }
            """,
        ["sepia"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half3 s = half3(dot(c.rgb, half3(0.393, 0.769, 0.189)),
                                dot(c.rgb, half3(0.349, 0.686, 0.168)),
                                dot(c.rgb, half3(0.272, 0.534, 0.131)));
                c.rgb = clamp(mix(c.rgb, s, half(amount)), 0.0, 1.0);
                return c;
            }
            """,
        ["invert"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) { half4 c = src.eval(p); c.rgb = mix(c.rgb, half3(1.0) - c.rgb, half(amount)); return c; }
            """,
        ["hue"] = """
            uniform shader src; uniform float3 r0; uniform float3 r1; uniform float3 r2;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float3 v = float3(c.rgb);
                c.rgb = clamp(half3(dot(r0, v), dot(r1, v), dot(r2, v)), 0.0, 1.0);
                return c;
            }
            """,
        ["tint"] = """
            uniform shader src; uniform float3 tint; uniform float strength; uniform float preserve;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half3 n = mix(c.rgb, half3(tint), half(strength));
                if (preserve > 0.5) {
                    half lin = dot(c.rgb, half3(0.299, 0.587, 0.114));
                    half lout = dot(n, half3(0.299, 0.587, 0.114));
                    if (lout > 1e-6) n = clamp(n * clamp(lin / lout, 0.0, 1.15), 0.0, 1.0);
                }
                c.rgb = n;
                return c;
            }
            """,
        ["vignette"] = """
            uniform shader src; uniform float2 size; uniform float strength; uniform float3 vcolor;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                float2 q = p - 0.5;
                float2 mid = size * 0.5;
                float maxDist = length(mid);
                float2 d = (q - mid) / maxDist;
                float t = min(1.0, dot(d, d));
                float k = t * t * strength;
                c.rgb = mix(c.rgb, half3(vcolor), half(k));
                return c;
            }
            """,
        ["posterize"] = """
            uniform shader src; uniform float levelStep;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                c.rgb = clamp(half3(floor(float3(c.rgb) * 255.0 / levelStep + 0.5) * levelStep / 255.0), 0.0, 1.0);
                return c;
            }
            """,
        ["pixelate"] = """
            uniform shader src; uniform float block;
            half4 main(float2 p) {
                float2 q = floor(p / block) * block + 0.5;
                return src.eval(q);
            }
            """,
        ["flip"] = """
            uniform shader src; uniform float2 size; uniform float flipX; uniform float flipY;
            half4 main(float2 p) {
                float2 q = float2(flipX > 0.5 ? size.x - p.x : p.x, flipY > 0.5 ? size.y - p.y : p.y);
                return src.eval(q);
            }
            """,
        ["sharpen"] = """
            uniform shader src; uniform float amount;
            half4 main(float2 p) {
                half4 c = src.eval(p);
                half3 lap = c.rgb * 4.0 - src.eval(p + float2(0, -1)).rgb - src.eval(p + float2(0, 1)).rgb
                          - src.eval(p + float2(-1, 0)).rgb - src.eval(p + float2(1, 0)).rgb;
                c.rgb = clamp(c.rgb + lap * half(amount), 0.0, 1.0);
                return c;
            }
            """,
        ["crop"] = """
            uniform shader src; uniform float2 size; uniform float2 origin; uniform float2 span;
            half4 main(float2 p) {
                float2 q = origin + floor(floor(p) * span / size);
                return src.eval(q + 0.5);
            }
            """,
        ["cropPad"] = """
            uniform shader src; uniform float2 size; uniform float2 src0; uniform float2 off; uniform float2 content;
            uniform float scale; uniform float3 pad; uniform float empty;
            half4 main(float2 p) {
                float x = floor(p.x);
                float y = size.y - 1.0 - floor(p.y);   // visual y counts up from the bottom
                bool inside = empty < 0.5 && x >= off.x && x < off.x + content.x && y >= off.y && y < off.y + content.y;
                if (!inside) return half4(half3(pad), 1.0);
                float sx = clamp(src0.x + floor((x - off.x) / scale), 0.0, size.x - 1.0);
                float sy = clamp(src0.y + floor((y - off.y) / scale), 0.0, size.y - 1.0);
                return src.eval(float2(sx + 0.5, size.y - 1.0 - sy + 0.5));
            }
            """,
        ["transform"] = """
            uniform shader src; uniform float2 size; uniform float2 center; uniform float2 cs; uniform float invZoom;
            half4 main(float2 p) {
                float dx = p.x - size.x * 0.5;
                float dy = p.y - size.y * 0.5;
                float2 s = center + float2(dx * cs.x - dy * cs.y, dx * cs.y + dy * cs.x) * invZoom;
                s = clamp(s, float2(0.5), size - 0.5);
                return src.eval(s);
            }
            """,

        ["tx.dissolve"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t;
            half4 main(float2 p) { return mix(a.eval(p), b.eval(p), half(t)); }
            """,
        ["tx.fade"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t;
            half4 main(float2 p) {
                half4 c = t < 0.5 ? a.eval(p) : b.eval(p);
                c.rgb *= half(abs(2.0 * t - 1.0));
                return c;
            }
            """,
        ["tx.wipe"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float soft;
            half4 main(float2 p) {
                float x = p.x - 0.5;
                float edge = soft * size.x;
                float boundary = t * (size.x + 2.0 * edge) - edge;
                float mixv = edge <= 0.5 ? (floor(p.x) < floor(t * size.x + 0.5) ? 1.0 : 0.0)
                                         : clamp((boundary - x) / (2.0 * edge) + 0.5, 0.0, 1.0);
                return mix(a.eval(p), b.eval(p), half(mixv));
            }
            """,
        ["tx.curtain"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float panels; uniform float soft;
            half4 main(float2 p) {
                float x = p.x - 0.5;
                float panelW = size.x / panels;
                float edge = max(0.5, soft * panelW);
                float panel = floor(x / panelW);
                float local = x - panel * panelW;
                float center = panelW * 0.5;
                float sweep = mod(panel, 2.0) < 0.5 ? -1.0 : 1.0;
                float boundary = center + sweep * (panelW * 0.5 + edge) * (2.0 * t - 1.0);
                float mixv = clamp((sweep * (boundary - local)) / (2.0 * edge) + 0.5, 0.0, 1.0);
                return mix(a.eval(p), b.eval(p), half(mixv));
            }
            """,
        ["tx.iris"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float soft;
            half4 main(float2 p) {
                float2 mid = size * 0.5;
                float maxR = length(mid);
                float edge = max(0.5, soft * maxR);
                float radius = t * (maxR + 2.0 * edge) - edge;
                float dist = length((p - 0.5) - mid);
                float mixv = clamp((radius - dist) / (2.0 * edge) + 0.5, 0.0, 1.0);
                return mix(a.eval(p), b.eval(p), half(mixv));
            }
            """,
        ["tx.push"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float dir;
            half4 main(float2 p) {
                float2 px = floor(p);
                bool horizontal = dir < 1.5;
                float len = horizontal ? size.x : size.y;
                float shift = floor(t * len + 0.5);
                float back = len - shift;
                float sgn = (dir < 0.5 || (dir > 1.5 && dir < 2.5)) ? 1.0 : -1.0;   // left/up move towards the origin
                float2 bp = horizontal ? float2(px.x + sgn * back, px.y) : float2(px.x, px.y + sgn * back);
                float2 ap = horizontal ? float2(px.x + sgn * shift, px.y) : float2(px.x, px.y + sgn * shift);
                if (bp.x >= 0.0 && bp.x < size.x && bp.y >= 0.0 && bp.y < size.y) return b.eval(bp + 0.5);
                if (ap.x >= 0.0 && ap.x < size.x && ap.y >= 0.0 && ap.y < size.y) return a.eval(ap + 0.5);
                return half4(0.0, 0.0, 0.0, 1.0);
            }
            """,
        ["tx.slide"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t; uniform float dir;
            half4 main(float2 p) {
                float2 px = floor(p);
                float rest = 1.0 - t;
                float offX = dir < 0.5 ? -floor(rest * size.x + 0.5) : (dir < 1.5 ? floor(rest * size.x + 0.5) : 0.0);
                float offY = (dir > 1.5 && dir < 2.5) ? -floor(rest * size.y + 0.5) : (dir > 2.5 ? floor(rest * size.y + 0.5) : 0.0);
                float2 bp = px - float2(offX, offY);
                if (bp.x >= 0.0 && bp.x < size.x && bp.y >= 0.0 && bp.y < size.y) return b.eval(bp + 0.5);
                return a.eval(p);
            }
            """,
        ["tx.swirl"] = """
            uniform shader a; uniform shader b; uniform float2 size; uniform float t;
            uniform float2 center; uniform float radius; uniform float rotation; uniform float direction; uniform float easing;
            float ease(float v) {
                if (easing < 0.5) return v;
                if (easing < 1.5) return v * v;
                if (easing < 2.5) return 1.0 - (1.0 - v) * (1.0 - v);
                return v < 0.5 ? 2.0 * v * v : 1.0 - pow(-2.0 * v + 2.0, 2.0) / 2.0;
            }
            float2 twist(float2 d, float angle) {
                float c = cos(angle);
                float s = sin(angle);
                return clamp(center + float2(d.x * c - d.y * s, d.x * s + d.y * c), float2(0.5), size - 0.5);
            }
            half4 main(float2 p) {
                float e = ease(t);
                float2 d = p - center;
                float dist = length(d);
                float k = dist >= radius ? 0.0 : 1.0 - dist / radius;
                float falloff = k * k;
                half4 ca = a.eval(twist(d, direction * rotation * e * falloff));
                half4 cb = b.eval(twist(d, -direction * rotation * (1.0 - e) * falloff));
                return mix(ca, cb, half(e));
            }
            """,
    };
}

/// <summary>Disposes the intermediate shaders built while assembling one frame.</summary>
internal sealed class ShaderScope : IDisposable
{
    private readonly List<IDisposable> _items = new();

    public T Add<T>(T item) where T : IDisposable
    {
        _items.Add(item);
        return item;
    }

    public void Dispose()
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            _items[i].Dispose();
        _items.Clear();
    }
}
