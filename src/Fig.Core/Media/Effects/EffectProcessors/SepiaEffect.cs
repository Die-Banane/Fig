using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Applies the classic sepia transform, blendable via "amount".</summary>
    [Effect(EffectCatalog.Sepia, "Sepia", Category = "Color", Icon = "image-down", Description = "Warm, brown-toned monochrome.")]
    [EffectParam("amount", "Amount", Default = 1, Min = 0, Max = 1)]
    internal sealed class SepiaEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (amount < 1e-6)
                return frame;
            return PixelOps.MapColor(frame, new Sepia(amount));
        }

        private readonly record struct Sepia(double Amount) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
            {
                var sr = 0.393 * r + 0.769 * g + 0.189 * b;
                var sg = 0.349 * r + 0.686 * g + 0.168 * b;
                var sb = 0.272 * r + 0.534 * g + 0.131 * b;
                (r, g, b) = (r + (sr - r) * Amount, g + (sg - g) * Amount, b + (sb - b) * Amount);
            }
        }
    }
}
