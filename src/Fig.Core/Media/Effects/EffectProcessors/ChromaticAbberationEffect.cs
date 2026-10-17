using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Lens fringing: the red and blue channels are sampled from opposite sides of each pixel while green
    /// stays put. Offsets are fractions of the frame width so the look is identical at every resolution.
    /// In radial mode the shift grows with distance from the centre, like a real lens.
    /// </summary>
    [Effect(EffectCatalog.ChromaticAberration, "Chromatic Aberration", Icon = "sun",
        Description = "Split the red and blue channels apart like a lens fringe.")]
    [EffectParam("radial", "Radial", Default = 0, Kind = ParamKind.Bool)]
    [EffectParam("x_offset", "X Offset", Default = 0.01, Min = -0.05, Max = 0.05)]
    [EffectParam("y_offset", "Y Offset", Default = 0.01, Min = -0.05, Max = 0.05)]
    internal sealed class ChromaticAberration : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var radial = p.Bool("radial");
            var xOffset = p.Number("x_offset");
            var yOffset = p.Number("y_offset");
            if (Math.Abs(xOffset) < 1e-6 && Math.Abs(yOffset) < 1e-6)
                return frame;

            var w = frame.Width;
            var h = frame.Height;
            var src = frame.Pixels;
            var px = FramePool.Rent(w * h * 4);

            // both axes are scaled by the width, so equal x and y offsets give an equal-length diagonal shift
            var dx = xOffset * w;
            var dy = yOffset * w;

            if (radial)
            {
                var cx = (w - 1) / 2.0;
                var cy = (h - 1) / 2.0;
                var maxDist = Math.Max(1.0, Math.Sqrt(cx * cx + cy * cy));
                PixelOps.Rows(h, row =>
                {
                    var oy = dy * (row - cy) / maxDist;
                    for (var col = 0; col < w; col++)
                    {
                        var ox = dx * (col - cx) / maxDist;
                        Write(src, px, w, h, col, row, ox, oy);
                    }
                });
            }
            else
            {
                // the shift is the same everywhere, so work out each column's source once
                var shiftX = (int)Math.Round(dx);
                var shiftY = (int)Math.Round(dy);
                var redX = new int[w];
                var blueX = new int[w];
                for (var col = 0; col < w; col++)
                {
                    redX[col] = Math.Clamp(col - shiftX, 0, w - 1);
                    blueX[col] = Math.Clamp(col + shiftX, 0, w - 1);
                }

                PixelOps.Rows(h, row =>
                {
                    var redRow = Math.Clamp(row - shiftY, 0, h - 1) * w;
                    var blueRow = Math.Clamp(row + shiftY, 0, h - 1) * w;
                    var rowStart = row * w;
                    for (var col = 0; col < w; col++)
                    {
                        var i = (rowStart + col) * 4;
                        px[i] = src[(blueRow + blueX[col]) * 4];         // blue: sampled from the +offset side
                        px[i + 1] = src[i + 1];                          // green stays put
                        px[i + 2] = src[(redRow + redX[col]) * 4 + 2];   // red: sampled from the -offset side
                        px[i + 3] = src[i + 3];
                    }
                });
            }

            return new DecodedFrame { Width = w, Height = h, Pixels = px };
        }

        /// <summary>Writes one output pixel with red from (x - ox, y - oy) and blue from (x + ox, y + oy).</summary>
        private static void Write(byte[] src, byte[] dst, int w, int h, int col, int row, double ox, double oy)
        {
            var xr = Math.Clamp((int)Math.Round(col - ox), 0, w - 1);
            var yr = Math.Clamp((int)Math.Round(row - oy), 0, h - 1);
            var xb = Math.Clamp((int)Math.Round(col + ox), 0, w - 1);
            var yb = Math.Clamp((int)Math.Round(row + oy), 0, h - 1);

            var i = (row * w + col) * 4;
            dst[i] = src[(yb * w + xb) * 4];
            dst[i + 1] = src[i + 1];
            dst[i + 2] = src[(yr * w + xr) * 4 + 2];
            dst[i + 3] = src[i + 3];
        }
    }
}
