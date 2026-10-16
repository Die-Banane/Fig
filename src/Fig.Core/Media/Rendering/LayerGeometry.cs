using System;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>A layer's unscaled size, in frame heights (so a full-height square in a 16:9 frame is 1 × 1).</summary>
    public readonly record struct LayerSize(double Width, double Height);

    /// <summary>
    /// Where a drawn layer sits in the frame, matching how it is drawn: moved to its centre, turned, then scaled.
    /// Points are in frame units (0..1 on each axis); <c>aspect</c> is the frame's width over its height.
    /// </summary>
    public static class LayerGeometry
    {
        /// <summary>The layer's four corners, clockwise from the top left.</summary>
        public static (double X, double Y)[] Corners(LayerTransform layout, LayerSize size, double aspect)
        {
            var hw = size.Width * layout.Scale / 2;
            var hh = size.Height * layout.Scale / 2;
            (double, double) At(double x, double y) => ToFrame(layout, x, y, aspect);
            return [At(-hw, -hh), At(hw, -hh), At(hw, hh), At(-hw, hh)];
        }

        /// <summary>A point given in the layer's own units (frame heights, from its centre, before turning) in frame units.</summary>
        public static (double X, double Y) ToFrame(LayerTransform layout, double x, double y, double aspect)
        {
            var (sin, cos) = Math.SinCos(layout.Rotation * Math.PI / 180);
            return (layout.CenterX + (x * cos - y * sin) / aspect, layout.CenterY + x * sin + y * cos);
        }

        /// <summary>A frame point in the layer's own units: frame heights from its centre, with its turn undone.</summary>
        public static (double X, double Y) ToLocal(LayerTransform layout, double x, double y, double aspect)
        {
            var dx = (x - layout.CenterX) * aspect;
            var dy = y - layout.CenterY;
            var (sin, cos) = Math.SinCos(-layout.Rotation * Math.PI / 180);
            return (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        public static bool Contains(LayerTransform layout, LayerSize size, double x, double y, double aspect)
        {
            var (lx, ly) = ToLocal(layout, x, y, aspect);
            return Math.Abs(lx) <= size.Width * layout.Scale / 2 && Math.Abs(ly) <= size.Height * layout.Scale / 2;
        }
    }
}
