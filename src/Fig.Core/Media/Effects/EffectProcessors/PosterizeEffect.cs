using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Quantizes each channel to a number of levels.</summary>
    [Effect(EffectCatalog.Posterize, "Posterize", Category = "Color", Icon = "sliders-horizontal", Description = "Quantize colors to fewer levels.")]
    [EffectParam("levels", "Levels", Kind = ParamKind.Int, Default = 4, Min = 2, Max = 32)]
    internal sealed class PosterizeEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var levels = p.Int("levels");
            return PixelOps.MapColor(frame, new Posterize(255.0 / (levels - 1)));
        }

        private readonly record struct Posterize(double Step) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
                => (r, g, b) = (Math.Round(r / Step) * Step, Math.Round(g / Step) * Step, Math.Round(b / Step) * Step);
        }
    }
}
