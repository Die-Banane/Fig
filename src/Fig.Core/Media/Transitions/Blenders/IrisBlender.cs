using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>An expanding circle reveals the incoming clip from the center.</summary>
    [Transition(TransitionCatalog.Iris, "Iris", Icon = "circle-dot", Description = "Expand a circle from the center.")]
    [TransitionParam("soft", "Soft edge", Default = 0.1, Min = 0, Max = 0.5)]
    internal sealed class IrisBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.Iris;

        public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)
        {
            t01 = Math.Clamp(t01, 0, 1);
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t01 < 0.5 ? outgoing : incoming;
            var soft = parameters.TryGetValue("soft", out var s) ? Math.Clamp(s.AsDouble, 0, 0.5) : 0.1;

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var w = outgoing.Width;
            var h = outgoing.Height;
            var dst = FramePool.Rent(w * h * 4);

            var cx = w / 2.0;
            var cy = h / 2.0;
            var maxR = Math.Sqrt(cx * cx + cy * cy);
            var edge = Math.Max(0.5, soft * maxR);
            // Travel from -edge to maxR+edge so the soft band fully exits the circle's far
            // side: at t01=0 no pixel is incoming (center starts at mix 0), at t01=1 even the
            // corners are fully incoming. (Midpoint radius stays maxR/2.)
            var radius = t01 * (maxR + 2 * edge) - edge;
            var inner = radius - edge;   // inside this distance: fully incoming
            var outer = radius + edge;   // beyond this distance: fully outgoing
            PixelOps.Rows(h, y =>
            {
                var dy = y - cy;
                var row = y * w * 4;
                if (outer <= Math.Abs(dy))
                {
                    PixelOps.LerpBytes(dst, a, b, row, w * 4, 0);   // the whole row is outside the circle
                    return;
                }

                // x ranges that need no maths: fully outgoing at the ends, fully incoming in the middle
                var outHalf = Math.Sqrt(outer * outer - dy * dy);
                var outL = Math.Clamp((int)Math.Floor(cx - outHalf) + 1, 0, w);   // x < outL: outgoing
                var outR = Math.Clamp((int)Math.Ceiling(cx + outHalf), 0, w);      // x >= outR: outgoing
                var inL = outR;
                var inR = outR;
                if (inner > Math.Abs(dy))
                {
                    var inHalf = Math.Sqrt(inner * inner - dy * dy);
                    inL = Math.Clamp((int)Math.Ceiling(cx - inHalf), outL, outR);
                    inR = Math.Clamp((int)Math.Floor(cx + inHalf) + 1, inL, outR);
                }
                else
                {
                    inL = inR = (outL + outR) / 2;   // no fully-incoming stretch: just the soft band
                }

                PixelOps.LerpBytes(dst, a, b, row, outL * 4, 0);
                for (var x = outL; x < inL; x++)
                    SoftPixel(x);
                PixelOps.LerpBytes(dst, a, b, row + inL * 4, (inR - inL) * 4, 256);
                for (var x = inR; x < outR; x++)
                    SoftPixel(x);
                PixelOps.LerpBytes(dst, a, b, row + outR * 4, (w - outR) * 4, 0);

                void SoftPixel(int x)
                {
                    var dist = Math.Sqrt((x - cx) * (x - cx) + dy * dy);
                    var weight = PixelOps.Weight256(Math.Clamp((radius - dist) / (2 * edge) + 0.5, 0, 1));
                    PixelOps.LerpBytes(dst, a, b, row + x * 4, 4, weight);
                }
            });
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
