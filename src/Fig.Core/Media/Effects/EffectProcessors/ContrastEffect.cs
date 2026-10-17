using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Adjusts contrast around mid-gray (128).</summary>
    [Effect(EffectCatalog.Contrast, "Contrast", Category = "Color", Icon = "contrast", Description = "Adjust contrast around mid-gray.")]
    [EffectParam("amount", "Amount", Default = 1, Min = 0, Max = 2)]
    internal sealed class ContrastEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (Math.Abs(amount - 1) < 1e-6)
                return frame;
            return PixelOps.MapColor(frame, new Contrast(amount));
        }

        private readonly record struct Contrast(double Amount) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
                => (r, g, b) = ((r - 128) * Amount + 128, (g - 128) * Amount + 128, (b - 128) * Amount + 128);
        }

    }
}
