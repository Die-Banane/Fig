using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Sweeps the incoming clip in from the left. The boundary position follows t01
    /// (0 = fully outgoing, 1 = fully incoming); "soft" (fraction of the frame width)
    /// controls how wide the blended edge is.
    /// </summary>
    [Transition(TransitionCatalog.Wipe, "Wipe", Category = "Wipe", Icon = "arrow-right-left", Description = "Sweep the incoming clip in from the left.")]
    [TransitionParam("soft", "Soft edge", Default = 0.1, Min = 0, Max = 0.5)]
    internal sealed class WipeBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.Wipe;

        public DecodedFrame Blend(
            DecodedFrame outgoing,
            DecodedFrame incoming,
            double t01,
            IReadOnlyDictionary<string, ParamValue> parameters)
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

            var incomingCols = (int)Math.Round(t01 * w);
            var edge = soft * w;
            // The soft band is centered on the sweep boundary; make the boundary travel from
            // -edge to w+edge so the band fully exits the frame: at t01=0 nothing is incoming
            // yet, at t01=1 the whole frame is incoming. (Midpoint boundary stays at w/2.)
            var boundary = t01 * (w + 2 * edge) - edge;
            var weights = new int[w];
            for (var x = 0; x < w; x++)
            {
                // mix = 0 -> outgoing (a), mix = 1 -> incoming (b); columns left of the sweep
                // boundary have been taken over by the incoming clip. soft=0 is a hard edge.
                var mix = edge <= 0.5
                    ? (x < incomingCols ? 1.0 : 0.0)
                    : Math.Clamp((boundary - x) / (2 * edge) + 0.5, 0, 1);
                weights[x] = PixelOps.Weight256(mix);
            }
            PixelOps.ColumnBlend(dst, a, b, w, h, weights);
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
