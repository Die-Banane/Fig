using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Moves a "camera" over the picture: where it looks (center), how close it is (zoom), and how
    /// it is turned (rotation). Every value can be keyframed, so zooming in, holding, and zooming
    /// back out is just a handful of keyframes on one bar. Behaviours like "Zoom in" or "Pan" are
    /// presets of this effect rather than separate effects.
    /// </summary>
    [Effect(EffectCatalog.Transform, "Transform", Category = "Motion", Icon = "focus",
        Description = "Zoom, pan and rotate the picture. Keyframe it to animate.")]
    [EffectParam("centerX", "Center X", Default = 0.5, Min = 0, Max = 1)]
    [EffectParam("centerY", "Center Y", Default = 0.5, Min = 0, Max = 1)]
    [EffectParam("zoom", "Zoom", Default = 1, Min = 1, Max = 8)]
    [EffectParam("rotation", "Rotation", Default = 0, Min = -180, Max = 180)]
    internal sealed class TransformEffect : IEffectProcessor
    {
        public string TypeId => EffectCatalog.Transform;

        public DecodedFrame Apply(DecodedFrame frame, IReadOnlyDictionary<string, ParamValue> parameters, double localT)
        {
            var view = TransformMath.ViewFrom(parameters);
            if (view.IsIdentity)
                return frame;

            var w = frame.Width;
            var h = frame.Height;
            var src = frame.Pixels;
            var dst = FramePool.Rent(w * h * 4);

            var radians = view.RotationDegrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var centerPxX = view.CenterX * w;
            var centerPxY = view.CenterY * h;
            var inverseZoom = 1.0 / view.Zoom;

            PixelOps.Rows(h, y =>
            {
                var dy = y + 0.5 - h / 2.0;
                var row = y * w * 4;
                for (var x = 0; x < w; x++)
                {
                    var dx = x + 0.5 - w / 2.0;
                    var sx = Math.Clamp(centerPxX + (dx * cos - dy * sin) * inverseZoom - 0.5, 0, w - 1);
                    var sy = Math.Clamp(centerPxY + (dx * sin + dy * cos) * inverseZoom - 0.5, 0, h - 1);
                    var x0 = (int)sx;
                    var y0 = (int)sy;
                    var x1 = Math.Min(w - 1, x0 + 1);
                    var y1 = Math.Min(h - 1, y0 + 1);
                    var fx = sx - x0;
                    var fy = sy - y0;
                    var i = row + x * 4;
                    for (var c = 0; c < 4; c++)
                    {
                        var top = src[(y0 * w + x0) * 4 + c] * (1 - fx) + src[(y0 * w + x1) * 4 + c] * fx;
                        var bottom = src[(y1 * w + x0) * 4 + c] * (1 - fx) + src[(y1 * w + x1) * 4 + c] * fx;
                        dst[i + c] = (byte)(top * (1 - fy) + bottom * fy + 0.5);
                    }
                }
            });

            return new DecodedFrame { Width = w, Height = h, Pixels = dst };
        }
    }
}
