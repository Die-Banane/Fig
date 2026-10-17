using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Rotates hue by an angle in degrees (standard RGB rotation matrix).</summary>
    [Effect(EffectCatalog.Hue, "Hue", Category = "Color", Icon = "rotate-ccw-clock", Description = "Rotate the hue of the image.")]
    [EffectParam("degrees", "Degrees", Default = 0, Min = 0, Max = 360)]
    internal sealed class HueEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var degrees = p.Number("degrees");
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees < 1e-6 || degrees > 359.999)
                return frame;
            return PixelOps.MapColor(frame, Hue.Rotating(degrees));
        }

        /// <summary>A hue rotation as a colour matrix.</summary>
        private readonly record struct Hue(double A00, double A01, double A02, double A10, double A11, double A12,
            double A20, double A21, double A22) : IColorMap
        {
            public static Hue Rotating(double degrees)
            {
                var rad = degrees * Math.PI / 180.0;
                var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
                return new Hue(
                    0.213 + cos * 0.787 - sin * 0.213, 0.715 - cos * 0.715 - sin * 0.715, 0.072 - cos * 0.072 + sin * 0.928,
                    0.213 - cos * 0.213 + sin * 0.143, 0.715 + cos * 0.285 + sin * 0.140, 0.072 - cos * 0.072 - sin * 0.283,
                    0.213 - cos * 0.213 - sin * 0.787, 0.715 - cos * 0.715 + sin * 0.715, 0.072 + cos * 0.928 + sin * 0.072);
            }

            public void Map(ref double r, ref double g, ref double b)
                => (r, g, b) = (A00 * r + A01 * g + A02 * b, A10 * r + A11 * g + A12 * b, A20 * r + A21 * g + A22 * b);
        }
    }
}
