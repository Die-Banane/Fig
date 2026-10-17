using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Adjusts color saturation (0 = grayscale, 2 = oversaturated).</summary>
    [Effect(EffectCatalog.Saturation, "Saturation", Category = "Color", Icon = "droplet", Description = "Adjust color saturation.")]
    [EffectParam("amount", "Amount", Default = 1, Min = 0, Max = 2)]
    internal sealed class SaturationEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (Math.Abs(amount - 1) < 1e-6)
                return frame;
            return PixelOps.MapColor(frame, new Saturation(amount));
        }

        private readonly record struct Saturation(double Amount) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
            {
                var luma = 0.299 * r + 0.587 * g + 0.114 * b;
                (r, g, b) = (luma + (r - luma) * Amount, luma + (g - luma) * Amount, luma + (b - luma) * Amount);
            }
        }
    }
}
