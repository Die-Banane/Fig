using System;

namespace Fig.Core.Timeline
{
    /// <summary>Chooses the time ruler's scale for a zoom level.</summary>
    public static class RulerCalculator
    {
        /// <summary>
        /// A ruler scale: labelled major ticks, minor ticks between them. The minor step doubles as the
        /// timeline's snap grid, so what you see on the ruler is what things snap to.
        /// </summary>
        public readonly record struct Scale(double MajorSec, double MinorSec, int Divisions);

        /// <summary>
        /// Picks the scale for a zoom level, like an NLE ruler: majors land on frames, seconds or minutes
        /// (never awkward values), at least <paramref name="minMajorPx"/> apart so timecode labels fit.
        /// </summary>
        public static Scale PickScale(double pixelsPerSecond, double fps, double minMajorPx = 104)
        {
            if (pixelsPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond));
            var frame = 1.0 / Math.Max(1, fps);

            // (major in seconds, divisions). Frame-based majors keep sub-second ticks on whole frames.
            (double Major, int Div)[] steps =
            {
                (frame, 1), (2 * frame, 2), (5 * frame, 5), (10 * frame, 5), (15 * frame, 3),
                (1, 5), (2, 4), (5, 5), (10, 5), (15, 3), (30, 6),
                (60, 6), (120, 4), (300, 5), (600, 10), (1800, 6), (3600, 6),
            };
            foreach (var (major, div) in steps)
            {
                if (major * pixelsPerSecond >= minMajorPx)
                    return new Scale(major, major / div, div);
            }
            return new Scale(3600, 600, 6);
        }
    }
}
