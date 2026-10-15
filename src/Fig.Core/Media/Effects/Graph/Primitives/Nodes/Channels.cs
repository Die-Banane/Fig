using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("split-channels", "Split channels", Category = "Channels", Description = "Separate red, green and blue into three pictures.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("r", "R")]
    [PrimitiveOutput("g", "G")]
    [PrimitiveOutput("b", "B")]
    internal sealed class SplitChannelsPrimitive : IGraphPrimitive
    {
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext c)
        {
            var src = c.Input("image");
            var width = src.Width;
            var input = src.Pixels;
            DecodedFrame Channel(int offset) => PrimitiveMath.Fill(src, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    var v = input[i + offset];
                    px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
                }
            });
            return new Dictionary<string, DecodedFrame> { ["r"] = Channel(2), ["g"] = Channel(1), ["b"] = Channel(0) };
        }
    }

    [Primitive("merge-channels", "Merge channels", Category = "Channels", Description = "Combine three pictures into red, green and blue.")]
    [PrimitiveInput("r", "R")]
    [PrimitiveInput("g", "G")]
    [PrimitiveInput("b", "B")]
    [PrimitiveOutput("image", "Image")]
    internal sealed class MergeChannelsPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            DecodedFrame r = c.Input("r"), g = c.Input("g"), b = c.Input("b");
            if (g.Width != r.Width || g.Height != r.Height || b.Width != r.Width || b.Height != r.Height)
                return r;
            var width = r.Width;
            byte[] pr = r.Pixels, pg = g.Pixels, pb = b.Pixels;
            return PrimitiveMath.Fill(r, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    px[i] = pb[i]; px[i + 1] = pg[i + 1]; px[i + 2] = pr[i + 2]; px[i + 3] = 255;
                }
            });
        }
    }
}
