using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Crops a fraction off each edge of the frame, then fits the remaining region back into
    /// the frame preserving its aspect ratio. The space the crop freed (pillarbox/letterbox
    /// bars) is filled with a solid padding color, so the output keeps the original frame size.
    /// Cropping all four sides by the same fraction zooms the image to fill the frame (aspect
    /// is preserved, so no padding shows); cropping only sides or only top/bottom produces
    /// visible colored bars. Row order follows the codebase's bottom-up convention (row 0 is
    /// the bottom of the image), matching FrameCompositor.CropScaleNearest.
    /// </summary>
    [Effect(EffectCatalog.CropWithPadding, "Crop with Padding", Icon = "crop",
        Description = "Crop the edges and pad the freed space with a solid color.")]
    [EffectParam("left", "Crop left", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("right", "Crop right", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("top", "Crop top", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("bottom", "Crop bottom", Default = 0, Min = 0, Max = 0.5)]
    [EffectParam("color", "Padding color", Kind = ParamKind.Color, Default = 0xFF000000)]
    internal sealed class CropWithPaddingEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var w = frame.Width;
            var h = frame.Height;
            var left = p.Number("left");
            var right = p.Number("right");
            var top = p.Number("top");
            var bottom = p.Number("bottom");
            var color = p.Color("color");

            // Crop rect in visual coordinates (row 0 = bottom of the image):
            // x0/x1 are the horizontal crop edges, y0/y1 the visual bottom/top edges.
            var x0 = (int)Math.Round(w * left);
            var x1 = w - (int)Math.Round(w * right);
            var y0 = (int)Math.Round(h * bottom);
            var y1 = h - (int)Math.Round(h * top);
            x1 = Math.Max(x0, x1);
            y1 = Math.Max(y0, y1);
            var cw = x1 - x0;
            var ch = y1 - y0;

            if (cw >= w && ch >= h)
                return frame;   // nothing cropped — pass the frame through untouched

            var src = frame.Pixels;
            var px = FramePool.Rent(w * h * 4);
            var padR = (byte)(color >> 16);
            var padG = (byte)(color >> 8);
            var padB = (byte)color;

            if (cw <= 0 || ch <= 0)
            {
                // the crop consumed the whole frame: solid padding color
                FillPadding(px, w, h, padR, padG, padB);
                return new DecodedFrame { Width = w, Height = h, Pixels = px };
            }

            // Fit the cropped region back into the frame preserving aspect ratio (scale >= 1),
            // centered. Bars appear on the axes whose crop fraction is smaller.
            var scale = Math.Min(w / (double)cw, h / (double)ch);
            var contentW = cw * scale;
            var contentH = ch * scale;
            var offX = (w - contentW) / 2.0;
            var offY = (h - contentH) / 2.0;

            PixelOps.Rows(h, y =>
            {
                var dstRow = (h - 1 - y) * w * 4;
                for (var x = 0; x < w; x++)
                {
                    var i = dstRow + x * 4;
                    if (x >= offX && x < offX + contentW && y >= offY && y < offY + contentH)
                    {
                        var srcX = Math.Clamp(x0 + (int)((x - offX) / scale), 0, w - 1);
                        var srcY = Math.Clamp(y0 + (int)((y - offY) / scale), 0, h - 1);
                        var si = (h - 1 - srcY) * w * 4 + srcX * 4;
                        px[i] = src[si];
                        px[i + 1] = src[si + 1];
                        px[i + 2] = src[si + 2];
                        px[i + 3] = src[si + 3];
                    }
                    else
                    {
                        px[i] = padB;
                        px[i + 1] = padG;
                        px[i + 2] = padR;
                        px[i + 3] = 255;
                    }
                }
            });

            return new DecodedFrame { Width = w, Height = h, Pixels = px };
        }

        private static void FillPadding(byte[] px, int w, int h, byte r, byte g, byte b)
        {
            PixelOps.Rows(h, y =>
            {
                var row = y * w * 4;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x * 4;
                    px[i] = b;
                    px[i + 1] = g;
                    px[i + 2] = r;
                    px[i + 3] = 255;
                }
            });
        }
    }
}
