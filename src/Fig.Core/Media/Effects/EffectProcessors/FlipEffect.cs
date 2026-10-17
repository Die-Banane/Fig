using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Mirrors the image on the chosen axis.</summary>
    [Effect(EffectCatalog.Flip, "Flip", Icon = "flip-horizontal-2", Description = "Mirror the image.")]
    [EffectParam("axis", "Axis", Kind = ParamKind.List, Default = 0, Min = 0, Max = 2, Choices = new[] { "Horizontal", "Vertical", "Both" })]
    internal sealed class FlipEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var axis = p.Choice("axis");
            var flipX = axis is 0 or 2;
            var flipY = axis is 1 or 2;
            if (!flipX && !flipY)
                return frame;

            var w = frame.Width;
            var h = frame.Height;
            var columns = new int[w];
            for (var x = 0; x < w; x++)
                columns[x] = flipX ? w - 1 - x : x;
            return PixelOps.Remap(frame, columns, y => flipY ? h - 1 - y : y);
        }
    }
}
