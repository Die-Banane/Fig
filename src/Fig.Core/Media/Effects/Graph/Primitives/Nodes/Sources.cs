using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("input", "Input frame", Category = "Sources", Description = "The picture the effect is applied to.")]
    [PrimitiveOutput("image", "Image")]
    internal sealed class InputPrimitive : IGraphPrimitive
    {
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext c)
            => new Dictionary<string, DecodedFrame> { ["image"] = c.Source };
    }

    [Primitive("solid", "Solid color", Category = "Sources", Description = "A flat color the size of the picture.")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("color", "Color", Kind = ParamKind.Color, Default = 0xFF000000)]
    internal sealed class SolidPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var color = c.Color("color");
            byte b = (byte)color, g = (byte)(color >> 8), r = (byte)(color >> 16);
            var width = c.Source.Width;
            return PrimitiveMath.Fill(c.Source, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
                }
            });
        }
    }

    [Primitive("noise", "Noise", Category = "Sources", Description = "Animated blocky noise, as a mask.")]
    [PrimitiveOutput("mask", "Mask", GraphPortType.Mask)]
    [PrimitiveParam("scale", "Cells across", Kind = ParamKind.Int, Default = 24, Min = 2, Max = 200)]
    [PrimitiveParam("speed", "Changes per second", Default = 3, Min = 0, Max = 30)]
    internal sealed class NoisePrimitive : IGraphPrimitive
    {
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext c)
        {
            var width = c.Source.Width;
            var cell = Math.Max(1, width / Math.Max(2, c.Int("scale")));
            var seed = (int)Math.Floor(c.LocalT * c.Number("speed"));
            var frame = PrimitiveMath.Fill(c.Source, (px, start, y) =>
            {
                for (int x = 0; x < width; x++)
                {
                    var v = Byte(Noise(x / cell, y / cell, seed) * 255);
                    int i = start + x * 4;
                    px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
                }
            });
            return new Dictionary<string, DecodedFrame> { ["mask"] = frame };
        }
    }
}
