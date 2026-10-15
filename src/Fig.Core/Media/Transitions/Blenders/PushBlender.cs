using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>The incoming clip pushes the outgoing off-canvas from a chosen side.</summary>
    [Transition(TransitionCatalog.Push, "Push", Category = "Camera", Icon = "move-right", Description = "Incoming pushes the outgoing off-screen.")]
    [TransitionParam("direction", "Direction", Kind = ParamKind.List, Default = 0, Min = 0, Max = 3, Choices = new[] { "Left", "Right", "Up", "Down" })]
    internal sealed class PushBlender : ITransitionBlender
    {
        public string TypeId => TransitionCatalog.Push;

        public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)
        {
            t01 = Math.Clamp(t01, 0, 1);
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t01 < 0.5 ? outgoing : incoming;
            var dir = parameters.TryGetValue("direction", out var d) ? d.AsChoice : 0;

            var a = outgoing.Pixels;
            var b = incoming.Pixels;
            var w = outgoing.Width;
            var h = outgoing.Height;
            var dst = FramePool.Rent(w * h * 4);

            var shift = (int)Math.Round(t01 * (dir is 0 or 1 ? w : h));
            var incomingBack = (dir is 0 or 1 ? w : h) - shift;
            PixelOps.Rows(h, y =>
            {
                var row = y * w * 4;
                if (dir is 0 or 1)
                {
                    // one row = [incoming piece][outgoing piece][black]; each is a straight copy
                    int bx0, bx1, ax0, ax1;   // destination x ranges and the matching source offsets
                    int bOff, aOff;
                    if (dir == 0)
                    {
                        bOff = incomingBack; aOff = shift;
                        bx0 = Math.Max(0, -bOff); bx1 = Math.Min(w, w - bOff);
                        ax0 = Math.Max(0, -aOff); ax1 = Math.Min(w, w - aOff);
                    }
                    else
                    {
                        bOff = -incomingBack; aOff = -shift;
                        bx0 = Math.Max(0, -bOff); bx1 = Math.Min(w, w - bOff);
                        ax0 = Math.Max(0, -aOff); ax1 = Math.Min(w, w - aOff);
                    }
                    for (var x = 0; x < w;)
                    {
                        if (x >= bx0 && x < bx1)
                        {
                            Buffer.BlockCopy(b, row + (x + bOff) * 4, dst, row + x * 4, (bx1 - x) * 4);
                            x = bx1;
                        }
                        else if (x >= ax0 && x < ax1)
                        {
                            var stop = bx0 > x ? Math.Min(ax1, bx0) : ax1;
                            Buffer.BlockCopy(a, row + (x + aOff) * 4, dst, row + x * 4, (stop - x) * 4);
                            x = stop;
                        }
                        else
                        {
                            var stop = w;
                            if (bx0 > x) stop = Math.Min(stop, bx0);
                            if (ax0 > x) stop = Math.Min(stop, ax0);
                            if (stop <= x) stop = w;
                            for (var xx = x; xx < stop; xx++)
                            {
                                var i = row + xx * 4;
                                dst[i] = 0; dst[i + 1] = 0; dst[i + 2] = 0; dst[i + 3] = 255;
                            }
                            x = stop;
                        }
                    }
                    return;
                }

                // vertical pushes move whole rows
                var by = dir == 2 ? y + incomingBack : y - incomingBack;
                var ay = dir == 2 ? y + shift : y - shift;
                if (by >= 0 && by < h)
                    Buffer.BlockCopy(b, by * w * 4, dst, row, w * 4);
                else if (ay >= 0 && ay < h)
                    Buffer.BlockCopy(a, ay * w * 4, dst, row, w * 4);
                else
                    for (var i = row; i < row + w * 4; i += 4)
                    {
                        dst[i] = 0; dst[i + 1] = 0; dst[i + 2] = 0; dst[i + 3] = 255;
                    }
            });
            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
