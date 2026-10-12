using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Linearly blends the outgoing and incoming frames across the transition window.</summary>
    [Transition(TransitionCatalog.CrossDissolve, "Cross Dissolve", Description = "Blend outgoing and incoming clips over the cut.")]
    internal sealed class CrossDissolveBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.CrossDissolve;

        public DecodedFrame Blend(
            DecodedFrame outgoing,
            DecodedFrame incoming,
            double t01,
            IReadOnlyDictionary<string, ParamValue> parameters)
        {
            t01 = Math.Clamp(t01, 0, 1);
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t01 < 0.5 ? outgoing : incoming;

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var w = outgoing.Width;
            var h = outgoing.Height;
            var dst = FramePool.Rent(w * h * 4);
            var weight = PixelOps.Weight256(t01);
            PixelOps.Rows(h, y => PixelOps.LerpBytes(dst, a, b, y * w * 4, w * 4, weight));
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
