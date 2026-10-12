using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Twists both clips around a point while dissolving between them: the outgoing picture winds
    /// up as it fades out and the incoming picture unwinds as it fades in. The twist is strongest
    /// at the center and fades to nothing at the radius.
    /// </summary>
    [Transition(TransitionCatalog.Swirl, "Swirl", Icon = "tornado", DefaultDurationSec = 1.4,
        Description = "Spin the picture around a point while dissolving.")]
    [TransitionParam("centerX", "Center X", Default = 0.5, Min = 0, Max = 1)]
    [TransitionParam("centerY", "Center Y", Default = 0.5, Min = 0, Max = 1)]
    [TransitionParam("radius", "Radius", Default = 0.42, Min = 0.05, Max = 1)]
    [TransitionParam("rotation", "Rotation", Default = 90, Min = 0, Max = 720)]
    [TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0,
        Choices = new[] { "Clockwise", "Counterclockwise" })]
    [TransitionParam("easing", "Easing", Kind = ParamKind.List, Default = 3,
        Choices = new[] { "Linear", "Ease in", "Ease out", "Ease in & out" })]
    internal sealed class SwirlBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.Swirl;

        public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)
        {
            t01 = Math.Clamp(t01, 0, 1);
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t01 < 0.5 ? outgoing : incoming;
            if (t01 <= 0)
                return outgoing;
            if (t01 >= 1)
                return incoming;

            double Num(string key, double fallback) => parameters.TryGetValue(key, out var v) ? v.AsNumber : fallback;
            var centerX = Math.Clamp(Num("centerX", 0.5), 0, 1);
            var centerY = Math.Clamp(Num("centerY", 0.5), 0, 1);
            var radiusFrac = Math.Clamp(Num("radius", 0.42), 0.05, 1);
            var rotation = Num("rotation", 90) * Math.PI / 180.0;
            var direction = parameters.TryGetValue("direction", out var d) && d.AsChoice == 1 ? -1.0 : 1.0;
            var easing = parameters.TryGetValue("easing", out var e) ? e.AsChoice : 3;

            var p = Ease(t01, easing);
            var w = outgoing.Width;
            var h = outgoing.Height;
            var cx = centerX * w;
            var cy = centerY * h;
            var radius = radiusFrac * Math.Min(w, h);

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var dst = FramePool.Rent(w * h * 4);

            PixelOps.Rows(h, y =>
            {
                var dy = y + 0.5 - cy;
                for (var x = 0; x < w; x++)
                {
                    var dx = x + 0.5 - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    var falloff = dist >= radius ? 0 : Math.Pow(1 - dist / radius, 2);

                    // the outgoing clip winds up as the transition goes on; the incoming unwinds
                    var angleOut = direction * rotation * p * falloff;
                    var angleIn = -direction * rotation * (1 - p) * falloff;

                    var i = (y * w + x) * 4;
                    Sample(a, w, h, cx, cy, dx, dy, angleOut, out var ob, out var og, out var or);
                    Sample(b, w, h, cx, cy, dx, dy, angleIn, out var ib, out var ig, out var ir);
                    dst[i] = (byte)Math.Round(ob + (ib - ob) * p);
                    dst[i + 1] = (byte)Math.Round(og + (ig - og) * p);
                    dst[i + 2] = (byte)Math.Round(or + (ir - or) * p);
                    dst[i + 3] = 255;
                }
            });
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }

        private static void Sample(byte[] src, int w, int h, double cx, double cy, double dx, double dy, double angle,
            out double blue, out double green, out double red)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var sx = Math.Clamp(cx + dx * cos - dy * sin - 0.5, 0, w - 1);
            var sy = Math.Clamp(cy + dx * sin + dy * cos - 0.5, 0, h - 1);
            var x0 = (int)sx;
            var y0 = (int)sy;
            var x1 = Math.Min(w - 1, x0 + 1);
            var y1 = Math.Min(h - 1, y0 + 1);
            var fx = sx - x0;
            var fy = sy - y0;
            double Mix(int channel)
            {
                var top = src[(y0 * w + x0) * 4 + channel] * (1 - fx) + src[(y0 * w + x1) * 4 + channel] * fx;
                var bottom = src[(y1 * w + x0) * 4 + channel] * (1 - fx) + src[(y1 * w + x1) * 4 + channel] * fx;
                return top * (1 - fy) + bottom * fy;
            }
            blue = Mix(0);
            green = Mix(1);
            red = Mix(2);
        }

        private static double Ease(double t, int mode) => mode switch
        {
            1 => t * t,
            2 => 1 - (1 - t) * (1 - t),
            3 => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
            _ => t,
        };
    }
}
