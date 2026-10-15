using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    /// <summary>Base for primitives with a single "image" output.</summary>
    internal abstract class ImagePrimitive : IGraphPrimitive
    {
        public IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext context)
            => new Dictionary<string, DecodedFrame> { ["image"] = Run(context) };

        protected abstract DecodedFrame Run(PrimitiveContext c);
    }

    internal static class PrimitiveMath
    {
        public static double Luma(byte b, byte g, byte r) => 0.0722 * b + 0.7152 * g + 0.2126 * r;

        /// <summary>Fills a new frame row by row; the callback receives the row's first byte offset.</summary>
        public static DecodedFrame Fill(DecodedFrame like, Action<byte[], int, int> row)
        {
            var output = New(like);
            var width = like.Width;
            PixelOps.Rows(like.Height, y => row(output.Pixels, y * width * 4, y));
            return output;
        }

        public static double Wrap(double v) => v - Math.Floor(v);

        public static double Mirror(double v)
        {
            var m = v % 2;
            if (m < 0) m += 2;
            return m > 1 ? 2 - m : m;
        }
    }
}
