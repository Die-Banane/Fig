using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("mix", "Mix", Category = "Mix & mask", Description = "Combine two pictures, optionally through a mask.")]
    [PrimitiveInput("a", "A")]
    [PrimitiveInput("b", "B")]
    [PrimitiveInput("mask", "Mask", GraphPortType.Mask, Required = false)]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("amount", "Amount", Default = 0.5, Min = 0, Max = 1)]
    [PrimitiveParam("mode", "Mode", Kind = ParamKind.List, Default = 0, Choices = new[] { "Normal", "Add", "Multiply", "Screen" })]
    internal sealed class MixPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            DecodedFrame a = c.Input("a"), b = c.Input("b");
            var mask = c.OptionalInput("mask");
            var amount = c.Number("amount");
            var mode = c.Choice("mode");
            if (a.Width != b.Width || a.Height != b.Height || (mask is not null && (mask.Width != a.Width || mask.Height != a.Height)))
                return a;
            if (mask is null && amount <= 0) return a;
            var width = a.Width;
            byte[] pa = a.Pixels, pb = b.Pixels, pm = mask?.Pixels ?? Array.Empty<byte>();
            return PrimitiveMath.Fill(a, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    var w = amount * (mask is null ? 1 : pm[i] / 255.0);
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double va = pa[i + ch], vb = pb[i + ch];
                        double blended = mode switch
                        {
                            1 => va + vb,
                            2 => va * vb / 255,
                            3 => 255 - (255 - va) * (255 - vb) / 255,
                            _ => vb,
                        };
                        px[i + ch] = Byte(va + (blended - va) * w);
                    }
                    px[i + 3] = pa[i + 3];
                }
            });
        }
    }

    [Primitive("mask-luma", "Mask from luma", Category = "Mix & mask", Description = "Turn brightness into a mask.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("mask", "Mask", GraphPortType.Mask)]
    [PrimitiveParam("threshold", "Threshold", Default = 0.5, Min = 0, Max = 1)]
    [PrimitiveParam("softness", "Softness", Default = 0.1, Min = 0.001, Max = 0.5)]
    [PrimitiveParam("invert", "Invert", Kind = ParamKind.Bool, Default = 0)]
    internal sealed class MaskLumaPrimitive : IGraphPrimitive
    {
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext c)
        {
            var src = c.Input("image");
            double threshold = c.Number("threshold"), softness = c.Number("softness");
            var invert = c.Bool("invert");
            var width = src.Width;
            var input = src.Pixels;
            var mask = PrimitiveMath.Fill(src, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    var luma = PrimitiveMath.Luma(input[i], input[i + 1], input[i + 2]) / 255;
                    var t = Math.Clamp((luma - (threshold - softness / 2)) / softness, 0, 1);
                    var v = Byte((invert ? 1 - t : t) * 255);
                    px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
                }
            });
            return new Dictionary<string, DecodedFrame> { ["mask"] = mask };
        }
    }
}
