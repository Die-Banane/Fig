using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("blur", "Blur", Category = "Blur & sharpen", Description = "Soften detail with a fast two-pass blur.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("radius", "Radius", Default = 0.012, Min = 0, Max = 0.08)]
    internal sealed class BlurPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var radius = (int)Math.Round(c.Number("radius") * Math.Min(src.Width, src.Height));
            return radius <= 0 ? src : Blur(src, radius);
        }
    }

    [Primitive("directional-blur", "Directional blur", Category = "Blur & sharpen", Description = "Smear the picture along an angle.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("distance", "Distance", Default = 0.025, Min = 0, Max = 0.15)]
    [PrimitiveParam("angle", "Angle", Default = 0, Min = -180, Max = 180)]
    internal sealed class DirectionalBlurPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var distance = c.Number("distance") * src.Width;
            if (distance <= 0) return src;
            var angle = c.Number("angle") * Math.PI / 180;
            double sx = Math.Cos(angle) * distance / 4, sy = Math.Sin(angle) * distance / 4;
            return Shade(src, (x, y, rgb) =>
            {
                for (int k = -4; k <= 4; k++)
                    SampleRgb(src, x + sx * k, y + sy * k, 1.0 / 9, rgb);
            });
        }
    }
}
