using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Desaturates to monochrome using Rec. 601 luma (blendable via "amount").</summary>
    [Effect(EffectCatalog.Grayscale, "Grayscale", Category = "Color", Icon = "contrast", Description = "Desaturate to monochrome.")]
    [EffectParam("amount", "Amount", Default = 1, Min = 0, Max = 1)]
    internal sealed class GrayscaleEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (amount < 1e-6)
                return frame;
            return PixelOps.MapColor(frame, new Grayscale(amount));
        }

        private readonly record struct Grayscale(double Amount) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
            {
                // Rec. 601 luma, truncated
                double y = Math.Clamp((int)(0.299 * r + 0.587 * g + 0.114 * b), 0, 255);
                (r, g, b) = Amount >= 1 ? (y, y, y) : (r + (y - r) * Amount, g + (y - g) * Amount, b + (y - b) * Amount);
            }
        }

    }
}
