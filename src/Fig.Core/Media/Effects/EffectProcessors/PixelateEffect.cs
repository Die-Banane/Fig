using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Mosaics the image into square blocks.</summary>
    [Effect(EffectCatalog.Pixelate, "Pixelate", Icon = "grid-3x3", Description = "Mosaic into blocks.")]
    [EffectParam("block", "Block size", Kind = ParamKind.Int, Default = 8, Min = 1, Max = 64)]
    internal sealed class PixelateEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var block = p.Int("block");
            if (block == 1)
                return frame;

            var columns = new int[frame.Width];
            for (var x = 0; x < columns.Length; x++)
                columns[x] = x / block * block;
            return PixelOps.Remap(frame, columns, y => y / block * block);
        }
    }
}
