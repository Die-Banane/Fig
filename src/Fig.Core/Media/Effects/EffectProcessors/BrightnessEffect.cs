using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Lifts or crushes midtones by adding a fixed delta to every channel.</summary>
    [Effect(EffectCatalog.Brightness, "Brightness", Category = "Color", Icon = "sun", Description = "Lift or crush midtones.")]
    [EffectParam("amount", "Amount", Default = 0.15, Min = -1, Max = 1)]
    internal sealed class BrightnessEffect : EffectProcessor
    {
        protected override DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT)
        {
            var amount = p.Number("amount");
            if (Math.Abs(amount) < 1e-6)
                return frame;

            var delta = (int)Math.Round(amount * 255);
            var src = frame.Pixels;
            var px = FramePool.Rent(frame.Width * frame.Height * 4);
            PixelOps.AddSaturateRgb(px, src, delta);
            return new DecodedFrame { Width = frame.Width, Height = frame.Height, Pixels = px };
        }
    }
}
