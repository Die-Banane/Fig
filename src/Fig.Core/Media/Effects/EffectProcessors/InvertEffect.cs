using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Inverts (negates) the RGB channels, blendable via "amount".</summary>
    [Effect(EffectCatalog.Invert, "Invert", Category = "Color", Icon = "circle-slash-2", Description = "Invert the image colors.")]
    [EffectParam("amount", "Amount", Default = 1, Min = 0, Max = 1)]
    internal sealed class InvertEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (amount < 1e-6)
                return frame;

            if (amount < 0.9999)
                return PixelOps.MapColor(frame, new Invert(amount));
            // all the way: the SIMD path
            var px = FramePool.Rent(frame.Width * frame.Height * 4);
            PixelOps.InvertRgbFull(px, frame.Pixels);
            return new DecodedFrame { Width = frame.Width, Height = frame.Height, Pixels = px };
        }

        private readonly record struct Invert(double Amount) : IColorMap
        {
            // lerp(src, 255 - src, amount)
            public void Map(ref double r, ref double g, ref double b)
                => (r, g, b) = (r + (255 - 2 * r) * Amount, g + (255 - 2 * g) * Amount, b + (255 - 2 * b) * Amount);
        }
    }
}
