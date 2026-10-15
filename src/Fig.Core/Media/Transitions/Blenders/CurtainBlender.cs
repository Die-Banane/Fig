using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Vertical curtain panels sweep in opposite directions to reveal the incoming clip.</summary>
    [Transition(TransitionCatalog.Curtain, "Curtain", Category = "Wipe", Icon = "columns-2", Description = "Open vertical curtain panels.")]
    [TransitionParam("panels", "Panels", Kind = ParamKind.Int, Default = 6, Min = 2, Max = 24)]
    [TransitionParam("soft", "Soft edge", Default = 0.1, Min = 0, Max = 0.5)]
    internal sealed class CurtainBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.Curtain;

        public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)
        {
            t01 = Math.Clamp(t01, 0, 1);
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t01 < 0.5 ? outgoing : incoming;
            var panels = parameters.TryGetValue("panels", out var p) ? Math.Max(2, p.AsInt) : 6;
            var soft = parameters.TryGetValue("soft", out var s) ? Math.Clamp(s.AsDouble, 0, 0.5) : 0.1;

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var w = outgoing.Width;
            var h = outgoing.Height;
            var dst = FramePool.Rent(w * h * 4);

            var panelW = w / (double)panels;
            var edge = Math.Max(0.5, soft * panelW);
            var weights = new int[w];
            for (var x = 0; x < w; x++)
            {
                var panel = (int)(x / panelW);
                var local = x - panel * panelW;
                var center = panelW / 2;
                var sweep = panel % 2 == 0 ? -1 : 1;
                // Sweep past each panel's far edge by the edge width so the soft band fully
                // exits: at t01=0 the whole frame is outgoing, at t01=1 fully incoming.
                var boundary = center + sweep * (panelW / 2 + edge) * (2 * t01 - 1);
                // mix = 1 (incoming) on the side the curtain has already opened past the boundary
                weights[x] = PixelOps.Weight256(Math.Clamp((sweep * (boundary - local)) / (2 * edge) + 0.5, 0, 1));
            }
            PixelOps.ColumnBlend(dst, a, b, w, h, weights);
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
