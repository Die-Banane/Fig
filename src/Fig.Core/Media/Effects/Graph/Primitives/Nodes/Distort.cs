using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("uv-offset", "UV offset", Category = "Distort", Description = "Shift the picture sideways or up and down.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("x", "Offset X", Default = 0, Min = -0.5, Max = 0.5)]
    [PrimitiveParam("y", "Offset Y", Default = 0, Min = -0.5, Max = 0.5)]
    [PrimitiveParam("edge", "Edge", Kind = ParamKind.List, Default = 0, Choices = new[] { "Clamp", "Wrap", "Mirror" })]
    internal sealed class UvOffsetPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            double ox = c.Number("x"), oy = c.Number("y");
            if (ox == 0 && oy == 0) return src;
            var edge = c.Choice("edge");
            Func<double, double> fold = edge switch
            {
                1 => PrimitiveMath.Wrap,
                2 => PrimitiveMath.Mirror,
                _ => v => v,
            };
            return Warp(src, (u, v) => (fold(u + ox), fold(v + oy)));
        }
    }

    [Primitive("lens-warp", "Lens warp", Category = "Distort", Description = "Barrel or pincushion distortion.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("amount", "Distortion", Default = 0.3, Min = -0.6, Max = 0.6)]
    internal sealed class LensWarpPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var amount = c.Number("amount");
            if (amount == 0) return src;
            var fit = amount > 0 ? 1 + amount * 2 : 1;
            return Warp(src, (u, v) =>
            {
                double x = u - 0.5, y = v - 0.5;
                var scale = Math.Max(0, 1 + amount * (x * x + y * y) * 4) / fit;
                return (0.5 + x * scale, 0.5 + y * scale);
            });
        }
    }

    [Primitive("mirror", "Mirror", Category = "Distort", Description = "Reflect half the picture or make four quadrants.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("axis", "Reflection", Kind = ParamKind.List, Default = 0, Choices = new[] { "Left to right", "Top to bottom", "Four quadrants" })]
    internal sealed class MirrorPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var axis = c.Choice("axis");
            return Warp(src, (u, v) => (axis != 1 ? Math.Min(u, 1 - u) : u, axis != 0 ? Math.Min(v, 1 - v) : v));
        }
    }
}
