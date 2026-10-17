using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Tints the image toward a target color. "strength" blends the tint in; "preserve_luma"
    /// rebalances the result so the original luma is roughly kept.
    /// </summary>
    [Effect(EffectCatalog.Tint, "Tint", Category = "Color", Icon = "palette", Description = "Tint the image toward a color.")]
    [EffectParam("color", "Color", Kind = ParamKind.Color, Default = 0xFF0080FF)]
    [EffectParam("strength", "Strength", Default = 0.5, Min = 0, Max = 1)]
    [EffectParam("preserve_luma", "Preserve luma", Kind = ParamKind.Bool, Default = 0)]
    internal sealed class TintEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var color = p.Color("color");
            var strength = p.Number("strength");
            if (strength < 1e-6)
                return frame;
            var preserveLuma = p.Bool("preserve_luma");
            return PixelOps.MapColor(frame, new Tint((byte)(color >> 16), (byte)(color >> 8), (byte)color, strength, preserveLuma));
        }

        private readonly record struct Tint(byte R, byte G, byte B, double Strength, bool PreserveLuma) : IColorMap
        {
            public void Map(ref double r, ref double g, ref double b)
            {
                var nr = (double)(byte)Math.Round(r + (R - r) * Strength);
                var ng = (double)(byte)Math.Round(g + (G - g) * Strength);
                var nb = (double)(byte)Math.Round(b + (B - b) * Strength);
                if (PreserveLuma)
                {
                    var lumaIn = 0.299 * r + 0.587 * g + 0.114 * b;
                    var lumaOut = 0.299 * nr + 0.587 * ng + 0.114 * nb;
                    if (lumaOut > 1e-6)
                    {
                        var ratio = Math.Clamp(lumaIn / lumaOut, 0, 1.15);
                        (nr, ng, nb) = (nr * ratio, ng * ratio, nb * ratio);
                    }
                }
                (r, g, b) = (nr, ng, nb);
            }
        }
    }
}
