using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Slides the incoming clip in over the outgoing from a chosen side.</summary>
    [Transition(TransitionCatalog.Slide, "Slide", Category = "Camera", Icon = "move-horizontal", Description = "Slide the incoming clip over the outgoing.")]
    [TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0, Min = 0, Max = 3, Choices = new[] { "Left", "Right", "Up", "Down" })]
    internal sealed class SlideBlender : TransitionBlender
    {
        protected override DecodedFrame Render(DecodedFrame outgoing, DecodedFrame incoming, double t01, ParamSet parameters)
        {
            var dir = parameters.Choice("direction");

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var w = outgoing.Width;
            var h = outgoing.Height;
            var dst = FramePool.Rent(w * h * 4);
            // incoming edge sits off-canvas and moves to 0: left/up are negative, right/down positive
            var offX = dir == 0 ? -(int)Math.Round((1 - t01) * w)
                      : dir == 1 ? (int)Math.Round((1 - t01) * w)
                      : 0;
            var offY = dir == 2 ? -(int)Math.Round((1 - t01) * h)
                      : dir == 3 ? (int)Math.Round((1 - t01) * h)
                      : 0;

            PixelOps.Rows(h, y =>
            {
                var row = y * w * 4;
                var by = y - offY;
                if (by < 0 || by >= h)
                {
                    Buffer.BlockCopy(a, row, dst, row, w * 4);
                    return;
                }
                // incoming covers destination x in [offX, offX + w) clipped to the frame
                var x0 = Math.Clamp(offX, 0, w);
                var x1 = Math.Clamp(offX + w, 0, w);
                Buffer.BlockCopy(a, row, dst, row, x0 * 4);
                Buffer.BlockCopy(b, by * w * 4 + (x0 - offX) * 4, dst, row + x0 * 4, (x1 - x0) * 4);
                Buffer.BlockCopy(a, row + x1 * 4, dst, row + x1 * 4, (w - x1) * 4);
            });
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
