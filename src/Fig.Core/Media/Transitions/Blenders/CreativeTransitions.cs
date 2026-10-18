using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media;

internal enum CreativeTransitionKind { Flash, Blur, ZoomIn, ZoomOut, Whip, Spin, Glitch, Pixel, Luma, Radial, Blinds, Flip }

/// <summary>Stateless shared render path; every transition has exact, unmodified endpoints.</summary>
internal abstract class CreativeTransitionBlender : TransitionBlender
{
    private protected abstract CreativeTransitionKind Kind { get; }

    protected override DecodedFrame Render(DecodedFrame a, DecodedFrame b, double t, ParamSet p)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        double progress = t * t * (3 - 2 * t);
        double peak = Math.Sin(Math.PI * t);
        // the kinds share this code but not their parameters (a whip pan has no amount, a flip has nothing)
        double amount = p.Declares("amount") ? p.Number("amount") : 0;
        double direction = p.Declares("direction") && p.Choice("direction") == 1 ? -1 : 1;

        if (Kind == CreativeTransitionKind.Flash)
        {
            var mixed = Mix(a, b, (_, _) => progress);
            try
            {
                double flash = Math.Pow(peak, 6) * amount;
                return Rgb(mixed, (x, y, c) => mixed.Pixels[(y * a.Width + x) * 4 + c] * (1 - flash) + 255 * flash);
            }
            finally { FramePool.Return(mixed.Pixels); }
        }
        if (Kind == CreativeTransitionKind.Blur)
        {
            var mixed = Mix(a, b, (_, _) => progress);
            int radius = (int)Math.Round(peak * amount * Math.Min(a.Width, a.Height) * 0.08);
            if (radius == 0) return mixed;
            try { return Blur(mixed, radius); }
            finally { FramePool.Return(mixed.Pixels); }
        }
        if (Kind is CreativeTransitionKind.ZoomIn or CreativeTransitionKind.ZoomOut or CreativeTransitionKind.Whip or CreativeTransitionKind.Spin or CreativeTransitionKind.Glitch or CreativeTransitionKind.Flip)
            return Geometry(a, b, t, progress, peak, amount, direction);

        double softness = p.Number("softness");
        double Smooth(double value) => Math.Clamp(0.5 + value / softness, 0, 1);
        // Loop-invariant parameters are resolved once, not per pixel.
        int columns = Kind == CreativeTransitionKind.Pixel ? p.Int("tiles") : 1;
        int rows = Math.Max(1, (int)Math.Round(columns * (double)a.Height / a.Width));
        double stripes = Kind == CreativeTransitionKind.Blinds ? p.Number("stripes") : 1;
        // Extending the threshold range by the softness margin gives clean endpoints.
        double reveal = progress * (1 + softness) - softness / 2;
        return Mix(a, b, (x, y) =>
        {
            double u = (x + 0.5) / a.Width, v = (y + 0.5) / a.Height;
            switch (Kind)
            {
                case CreativeTransitionKind.Pixel:
                    double noise = Noise((int)(u * columns), (int)(v * rows), 17);
                    return Smooth(reveal - noise);
                case CreativeTransitionKind.Luma:
                    int i = (y * a.Width + x) * 4;
                    double luma = (0.0722 * a.Pixels[i] + 0.7152 * a.Pixels[i + 1] + 0.2126 * a.Pixels[i + 2]) / 255;
                    return Smooth(reveal - (1 - luma));
                case CreativeTransitionKind.Radial:
                    double angle = (Math.Atan2(v - 0.5, (u - 0.5) * a.Width / a.Height) / (2 * Math.PI) + 1.25) % 1;
                    if (direction < 0) angle = 1 - angle;
                    return Smooth(reveal - angle);
                case CreativeTransitionKind.Blinds:
                    double pos = u * stripes;
                    return Smooth(reveal - (pos - Math.Floor(pos)));
                default: return progress;
            }
        });
    }

    private DecodedFrame Geometry(DecodedFrame a, DecodedFrame b, double t, double progress, double peak, double amount, double direction)
    {
        if (Kind == CreativeTransitionKind.Flip)
        {
            var source = t < 0.5 ? a : b;
            double width = Math.Max(0.001, Math.Abs(Math.Cos(Math.PI * t)));
            var output = New(source);
            PixelOps.Rows(a.Height, y =>
            {
                for (int x = 0; x < a.Width; x++)
                {
                    double u = (x + 0.5) / a.Width;
                    bool inside = Math.Abs(u - 0.5) <= width / 2;
                    int index = (y * a.Width + x) * 4;
                    for (int c = 0; c < 3; c++)
                        output.Pixels[index + c] = inside ? Byte(Sample(source, (0.5 + (u - 0.5) / width) * a.Width - 0.5, y, c)) : (byte)0;
                    output.Pixels[index + 3] = source.Pixels[index + 3];
                }
            });
            return output;
        }

        (double X, double Y) Map(double u, double v, bool incoming)
        {
            if (Kind is CreativeTransitionKind.ZoomIn or CreativeTransitionKind.ZoomOut)
            {
                double sign = Kind == CreativeTransitionKind.ZoomIn ? 1 : -1;
                double scale = Math.Exp(sign * amount * 1.5 * (incoming ? 1 - progress : -progress));
                // the zoomed-out side is smaller than the frame: mirror it out to the edges
                return (Mirror(0.5 + (u - 0.5) * scale), Mirror(0.5 + (v - 0.5) * scale));
            }
            if (Kind == CreativeTransitionKind.Whip)
            {
                double shift = direction * (incoming ? 1 - progress : -progress);
                double wrap = u + shift;
                return (wrap - Math.Floor(wrap), v);
            }
            if (Kind == CreativeTransitionKind.Spin)
            {
                double angle = direction * amount * Math.PI * (incoming ? 1 - progress : -progress);
                double aspect = (double)a.Width / a.Height;
                double x = (u - 0.5) * aspect, y = v - 0.5;
                double zoom = 1 + peak * amount;
                return (0.5 + (x * Math.Cos(angle) - y * Math.Sin(angle)) / aspect / zoom,
                    0.5 + (x * Math.Sin(angle) + y * Math.Cos(angle)) / zoom);
            }
            // Slice glitch uses reproducible bands so scrubbing and export match.
            int band = (int)(v * 24), seed = (int)(t * 24);
            double shiftGlitch = (Noise(0, band, seed) - 0.5) * peak * amount * 0.35;
            return (u + shiftGlitch, v);
        }

        var warpedA = Warp(a, (u, v) => Map(u, v, false));
        DecodedFrame? warpedB = null;
        try
        {
            warpedB = Warp(b, (u, v) => Map(u, v, true));
            return Mix(warpedA, warpedB, (_, _) => progress);
        }
        finally
        {
            FramePool.Return(warpedA.Pixels);
            if (warpedB is not null) FramePool.Return(warpedB.Pixels);
        }
    }
}

[Transition("white-flash", "White Flash", Category = "Light", Icon = "zap", DefaultDurationSec = 0.35, PreviewProgress = 0.35, Description = "A bright flash bridges the cut.")]
[TransitionParam("amount", "Flash intensity", Default = 1, Min = 0, Max = 1)]
internal sealed class WhiteFlashBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Flash; }

[Transition("blur-dissolve", "Blur Dissolve", Category = "Blur", Icon = "droplets", DefaultDurationSec = 0.6, Description = "Defocus both shots into a soft dissolve.")]
[TransitionParam("amount", "Blur strength", Default = 0.5, Min = 0, Max = 1)]
internal sealed class BlurDissolveBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Blur; }

[Transition("zoom-in", "Zoom In", Category = "Camera", Icon = "zoom-in", DefaultDurationSec = 0.45, Description = "Push through the cut into the next shot.")]
[TransitionParam("amount", "Zoom strength", Default = 0.55, Min = 0, Max = 1)]
internal sealed class ZoomInBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.ZoomIn; }

[Transition("zoom-out", "Zoom Out", Category = "Camera", Icon = "zoom-out", DefaultDurationSec = 0.45, Description = "Pull back through the cut to reveal the next shot.")]
[TransitionParam("amount", "Zoom strength", Default = 0.55, Min = 0, Max = 1)]
internal sealed class ZoomOutBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.ZoomOut; }

[Transition("whip-pan", "Whip Pan", Category = "Camera", Icon = "move-horizontal", DefaultDurationSec = 0.3, Description = "Wrap the image sideways across a fast camera pan.")]
[TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0, Choices = new[] { "Left", "Right" })]
internal sealed class WhipPanBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Whip; }

[Transition("spin", "Spin", Category = "Camera", Icon = "rotate-cw", DefaultDurationSec = 0.55, Description = "Rotate and zoom both shots through the cut.")]
[TransitionParam("amount", "Rotation strength", Default = 0.5, Min = 0, Max = 1)]
[TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0, Choices = new[] { "Clockwise", "Counterclockwise" })]
internal sealed class SpinBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Spin; }

[Transition("glitch-cut", "Glitch Cut", Category = "Glitch", Icon = "zap", DefaultDurationSec = 0.3, Description = "Displaced horizontal strips break up the cut.")]
[TransitionParam("amount", "Displacement", Default = 0.65, Min = 0, Max = 1)]
internal sealed class GlitchCutBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Glitch; }

[Transition("pixel-dissolve", "Pixel Dissolve", Category = "Glitch", Icon = "grid-3x3", DefaultDurationSec = 0.6, Description = "Small tiles reveal the next shot in a scattered pattern.")]
[TransitionParam("tiles", "Tile columns", Kind = ParamKind.Int, Default = 24, Min = 4, Max = 100)]
[TransitionParam("softness", "Edge softness", Default = 0.08, Min = 0.001, Max = 0.4)]
internal sealed class PixelDissolveBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Pixel; }

[Transition("luma-dissolve", "Luma Dissolve", Category = "Light", Icon = "sun", DefaultDurationSec = 0.7, Description = "Highlights reveal the incoming shot before shadows.")]
[TransitionParam("softness", "Edge softness", Default = 0.12, Min = 0.001, Max = 0.4)]
internal sealed class LumaDissolveBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Luma; }

[Transition("radial-wipe", "Clock Wipe", Category = "Wipe", Icon = "clock", DefaultDurationSec = 0.7, Description = "Sweep around the image center like a clock hand.")]
[TransitionParam("softness", "Edge softness", Default = 0.04, Min = 0.001, Max = 0.4)]
[TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0, Choices = new[] { "Clockwise", "Counterclockwise" })]
internal sealed class RadialWipeBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Radial; }

[Transition("blinds", "Venetian Blinds", Category = "Wipe", Icon = "columns-2", DefaultDurationSec = 0.65, Description = "Open repeating vertical strips onto the next shot.")]
[TransitionParam("stripes", "Strips", Kind = ParamKind.Int, Default = 10, Min = 2, Max = 40)]
[TransitionParam("softness", "Edge softness", Default = 0.08, Min = 0.001, Max = 0.4)]
internal sealed class BlindsBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Blinds; }

[Transition("card-flip", "Card Flip", Category = "Camera", Icon = "columns-2", DefaultDurationSec = 0.6, PreviewProgress = 0.35, Description = "Turn the picture edge-on, then unfold the next shot.")]
internal sealed class CardFlipBlender : CreativeTransitionBlender
{ private protected override CreativeTransitionKind Kind => CreativeTransitionKind.Flip; }
