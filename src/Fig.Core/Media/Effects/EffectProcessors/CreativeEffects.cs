using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media;

[Effect("soft-blur", "Soft Blur", Category = "Blur", Icon = "droplets", Description = "Soften detail with a fast two-pass blur.")]
[EffectParam("amount", "Radius", Default = 0.012, Min = 0, Max = 0.08)]
internal sealed class SoftBlurEffect : IEffectProcessor
{
    public string TypeId => "soft-blur";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.012, 0, 0.08);
        return amount == 0 ? f : Blur(f, Math.Max(1, (int)Math.Round(amount * Math.Min(f.Width, f.Height))));
    }
}

[Effect("directional-blur", "Directional Blur", Category = "Blur", Icon = "move-horizontal", Description = "Smear the picture along an adjustable angle.")]
[EffectParam("amount", "Distance", Default = 0.025, Min = 0, Max = 0.15)]
[EffectParam("angle", "Angle", Default = 0, Min = -180, Max = 180)]
internal sealed class DirectionalBlurEffect : IEffectProcessor
{
    public string TypeId => "directional-blur";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double distance = Number(p, "amount", 0.025, 0, 0.15) * f.Width;
        if (distance == 0) return f;
        double angle = Number(p, "angle", 0, -180, 180) * Math.PI / 180;
        // One tap step, shared by every pixel.
        double stepX = Math.Cos(angle) * distance / 4, stepY = Math.Sin(angle) * distance / 4;
        return Shade(f, (x, y, rgb) =>
        {
            for (int k = -4; k <= 4; k++)
                SampleRgb(f, x + stepX * k, y + stepY * k, 1.0 / 9, rgb);
        });
    }
}

[Effect("zoom-blur", "Zoom Blur", Category = "Blur", Icon = "focus", Description = "Streak details radially toward the center.")]
[EffectParam("amount", "Strength", Default = 0.15, Min = 0, Max = 0.7)]
internal sealed class ZoomBlurEffect : IEffectProcessor
{
    public string TypeId => "zoom-blur";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.15, 0, 0.7);
        if (amount == 0) return f;
        double cx = (f.Width - 1) / 2.0, cy = (f.Height - 1) / 2.0;
        return Shade(f, (x, y, rgb) =>
        {
            for (int k = 0; k < 10; k++)
            {
                double scale = 1 - amount * k / 9;
                SampleRgb(f, cx + (x - cx) * scale, cy + (y - cy) * scale, 0.1, rgb);
            }
        });
    }
}

[Effect("glow", "Glow", Category = "Light", Icon = "sun", Description = "Bloom bright highlights into a soft halo.")]
[EffectParam("amount", "Intensity", Default = 0.6, Min = 0, Max = 2)]
[EffectParam("radius", "Spread", Default = 0.025, Min = 0.002, Max = 0.1)]
[EffectParam("threshold", "Highlight threshold", Default = 0.65, Min = 0, Max = 1)]
internal sealed class GlowEffect : IEffectProcessor
{
    public string TypeId => "glow";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.6, 0, 2), threshold = Number(p, "threshold", 0.65) * 255;
        if (amount == 0) return f;
        var highlights = Rgb(f, (x, y, c) => Math.Max(0, f.Pixels[(y * f.Width + x) * 4 + c] - threshold));
        DecodedFrame? blurred = null;
        try
        {
            blurred = Blur(highlights, Math.Max(1, (int)(Number(p, "radius", 0.025, 0.002, 0.1) * Math.Min(f.Width, f.Height))));
            return Rgb(f, (x, y, c) => f.Pixels[(y * f.Width + x) * 4 + c] + blurred.Pixels[(y * f.Width + x) * 4 + c] * amount);
        }
        finally
        {
            FramePool.Return(highlights.Pixels);
            if (blurred is not null) FramePool.Return(blurred.Pixels);
        }
    }
}

[Effect("film-grain", "Film Grain", Category = "Retro", Icon = "film", Description = "Animated monochrome grain with repeatable results on export.")]
[EffectParam("amount", "Strength", Default = 0.15, Min = 0, Max = 1)]
[EffectParam("size", "Grain size", Kind = ParamKind.Int, Default = 1, Min = 1, Max = 6)]
internal sealed class FilmGrainEffect : IEffectProcessor
{
    public string TypeId => "film-grain";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.15) * 100;
        if (amount == 0) return f;
        int size = (int)Number(p, "size", 1, 1, 6), seed = (int)Math.Floor(t * 24);
        return Rgb(f, (x, y, c) => f.Pixels[(y * f.Width + x) * 4 + c] + (Noise(x / size, y / size, seed) - 0.5) * amount);
    }
}

[Effect("vhs", "VHS Tape", Category = "Retro", Icon = "tv", Description = "Rolling tape wobble, scanlines, color bleed and analog noise.")]
[EffectParam("amount", "Intensity", Default = 0.45, Min = 0, Max = 1)]
[EffectParam("speed", "Tracking speed", Default = 1, Min = 0.1, Max = 4)]
internal sealed class VhsEffect : IEffectProcessor
{
    public string TypeId => "vhs";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.45), speed = Number(p, "speed", 1, 0.1, 4);
        if (amount == 0) return f;
        int seed = (int)Math.Floor(t * 30);
        double bleedStep = f.Width * amount * 0.004;
        return Shade(f, (x, y, rgb) =>
        {
            double v = (double)y / f.Height;
            double shift = Math.Sin(v * 35 + t * speed * 8) * f.Width * amount * 0.006;
            double scan = 1 - amount * 0.18 * (0.5 + 0.5 * Math.Cos(v * 360 * Math.PI));
            double noise = (Noise(x, y, seed) - 0.5) * amount * 30;
            for (int c = 0; c < 3; c++)
                rgb[c] = Sample(f, x + shift + (c - 1) * bleedStep, y, c) * scan + noise;
        });
    }
}

[Effect("digital-glitch", "Digital Glitch", Category = "Glitch", Icon = "zap", Description = "Animated horizontal slice displacement and RGB separation.")]
[EffectParam("amount", "Intensity", Default = 0.4, Min = 0, Max = 1)]
[EffectParam("speed", "Changes per second", Default = 12, Min = 1, Max = 30)]
internal sealed class DigitalGlitchEffect : IEffectProcessor
{
    public string TypeId => "digital-glitch";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.4);
        if (amount == 0) return f;
        int seed = (int)Math.Floor(t * Number(p, "speed", 12, 1, 30));
        double split = f.Width * amount * 0.015;
        return Shade(f, (x, y, rgb) =>
        {
            int band = y * 24 / f.Height;
            double shift = Noise(0, band, seed) > 0.65 ? (Noise(1, band, seed) - 0.5) * f.Width * amount * 0.25 : 0;
            for (int c = 0; c < 3; c++)
                rgb[c] = Sample(f, x + shift + (c - 1) * split, y, c);
        });
    }
}

[Effect("camera-shake", "Camera Shake", Category = "Motion", Icon = "move", Description = "Handheld-style animated movement with protective zoom.")]
[EffectParam("amount", "Strength", Default = 0.025, Min = 0, Max = 0.12)]
[EffectParam("speed", "Speed", Default = 8, Min = 0.5, Max = 25)]
internal sealed class CameraShakeEffect : IEffectProcessor
{
    public string TypeId => "camera-shake";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.025, 0, 0.12), speed = Number(p, "speed", 8, 0.5, 25);
        if (amount == 0) return f;
        double dx = amount * (Math.Sin(t * speed * 6.28) + 0.4 * Math.Sin(t * speed * 13.7)) / 1.4;
        double dy = amount * (Math.Sin(t * speed * 8.1 + 1) + 0.4 * Math.Sin(t * speed * 17.3)) / 1.4;
        double zoom = 1 + amount * 5;
        return Warp(f, (u, v) => (0.5 + (u - 0.5) / zoom + dx, 0.5 + (v - 0.5) / zoom + dy));
    }
}

[Effect("pulse-zoom", "Pulse Zoom", Category = "Motion", Icon = "focus", Description = "Repeated camera pushes at an adjustable tempo.")]
[EffectParam("amount", "Zoom strength", Default = 0.12, Min = 0, Max = 0.8)]
[EffectParam("bpm", "Tempo (BPM)", Default = 120, Min = 30, Max = 240)]
internal sealed class PulseZoomEffect : IEffectProcessor
{
    public string TypeId => "pulse-zoom";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.12, 0, 0.8);
        if (amount == 0) return f;
        double pulse = Math.Pow(0.5 + 0.5 * Math.Cos(t * Number(p, "bpm", 120, 30, 240) / 60 * Math.PI * 2), 4);
        double zoom = 1 + amount * pulse;
        return Warp(f, (u, v) => (0.5 + (u - 0.5) / zoom, 0.5 + (v - 0.5) / zoom));
    }
}

[Effect("strobe", "Strobe Flash", Category = "Light", Icon = "zap", Description = "Short white flashes or dark pulses at a controlled rate.")]
[EffectParam("amount", "Intensity", Default = 0.7, Min = 0, Max = 1)]
[EffectParam("frequency", "Flashes per second", Default = 2, Min = 0.25, Max = 12)]
[EffectParam("dark", "Black flash", Kind = ParamKind.Bool, Default = 0)]
internal sealed class StrobeEffect : IEffectProcessor
{
    public string TypeId => "strobe";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double phase = t * Number(p, "frequency", 2, 0.25, 12);
        double flash = Math.Max(0, 1 - (phase - Math.Floor(phase)) / 0.2) * Number(p, "amount", 0.7);
        if (flash <= 0) return f;
        double target = p.TryGetValue("dark", out var dark) && dark.AsBool ? 0 : 255;
        return Rgb(f, (x, y, c) => f.Pixels[(y * f.Width + x) * 4 + c] * (1 - flash) + target * flash);
    }
}

[Effect("lens-distortion", "Lens Distortion", Category = "Distort", Icon = "aperture", Description = "Barrel or pincushion distortion around the image center.")]
[EffectParam("amount", "Distortion", Default = 0.3, Min = -0.6, Max = 0.6)]
internal sealed class LensDistortionEffect : IEffectProcessor
{
    public string TypeId => "lens-distortion";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.3, -0.6, 0.6);
        if (amount == 0) return f;
        // Barrel distortion pushes samples past the frame edge; a compensating zoom
        // keeps the farthest corner sample on the border so no clamped edge pixels show.
        double fit = amount > 0 ? 1 + amount * 2 : 1;
        return Warp(f, (u, v) =>
        {
            double x = u - 0.5, y = v - 0.5;
            double scale = Math.Max(0, 1 + amount * (x * x + y * y) * 4) / fit;
            return (0.5 + x * scale, 0.5 + y * scale);
        });
    }
}

[Effect("mirror", "Mirror", Category = "Distort", Icon = "columns-2", Description = "Reflect half the picture or create four mirrored quadrants.")]
[EffectParam("axis", "Reflection", Kind = ParamKind.List, Default = 0, Choices = new[] { "Left to right", "Top to bottom", "Four quadrants" })]
internal sealed class MirrorEffect : IEffectProcessor
{
    public string TypeId => "mirror";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        int axis = p.TryGetValue("axis", out var a) ? a.AsChoice : 0;
        return Warp(f, (u, v) => (axis != 1 ? Math.Min(u, 1 - u) : u, axis != 0 ? Math.Min(v, 1 - v) : v));
    }
}

[Effect("light-leak", "Light Leak", Category = "Light", Icon = "sunrise", Description = "Animated warm film light spilling in from the frame edge.")]
[EffectParam("amount", "Intensity", Default = 0.5, Min = 0, Max = 1)]
[EffectParam("speed", "Drift speed", Default = 0.5, Min = 0, Max = 3)]
internal sealed class LightLeakEffect : IEffectProcessor
{
    public string TypeId => "light-leak";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 0.5), speed = Number(p, "speed", 0.5, 0, 3);
        if (amount == 0) return f;
        double center = -0.12 + Math.Sin(t * speed) * 0.15;
        return Rgb(f, (x, y, c) =>
        {
            double u = (x + 0.5) / f.Width, v = (y + 0.5) / f.Height;
            double light = Math.Exp(-Math.Pow((u - center) / 0.3, 2) - Math.Pow((v - 0.4) / 0.8, 2)) * amount;
            double color = c == 2 ? 255 : c == 1 ? 110 : 35;
            double src = f.Pixels[(y * f.Width + x) * 4 + c];
            return 255 - (255 - src) * (1 - light * color / 255);
        });
    }
}

[Effect("duotone", "Duotone", Category = "Color", Icon = "palette", Description = "Map shadows and highlights to two chosen colors.")]
[EffectParam("amount", "Blend", Default = 1, Min = 0, Max = 1)]
[EffectParam("shadows", "Shadows", Kind = ParamKind.Color, Default = 0xFF14284D)]
[EffectParam("highlights", "Highlights", Kind = ParamKind.Color, Default = 0xFFFFC857)]
internal sealed class DuotoneEffect : IEffectProcessor
{
    public string TypeId => "duotone";
    public DecodedFrame Apply(DecodedFrame f, IReadOnlyDictionary<string, ParamValue> p, double t)
    {
        double amount = Number(p, "amount", 1);
        if (amount == 0) return f;
        uint low = p.TryGetValue("shadows", out var a) ? a.AsColor : 0xFF14284Du;
        uint high = p.TryGetValue("highlights", out var b) ? b.AsColor : 0xFFFFC857u;
        return Rgb(f, (x, y, c) =>
        {
            int i = (y * f.Width + x) * 4;
            double luma = (0.0722 * f.Pixels[i] + 0.7152 * f.Pixels[i + 1] + 0.2126 * f.Pixels[i + 2]) / 255;
            double color = ((low >> (c * 8)) & 255) * (1 - luma) + ((high >> (c * 8)) & 255) * luma;
            return f.Pixels[i + c] * (1 - amount) + color * amount;
        });
    }
}
