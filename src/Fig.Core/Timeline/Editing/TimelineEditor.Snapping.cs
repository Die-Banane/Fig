using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Snapping times to frames, and magnetic snapping to clip edges, markers, the playhead and the grid.
    /// </summary>
    public partial class TimelineEditor
    {
        public double SnapTime(double sec)
        {
            return FrameMath.SnapToFrame(sec, Document.Rate);
        }

        /// <summary>
        /// When enabled, drag/drop positions also snap to nearby clip boundaries
        /// (magnetic snapping) in addition to the frame grid.
        /// </summary>
        public bool MagneticSnap { get; set; }

        /// <summary>Default snap reach in seconds, used until the view sets a zoom-aware value.</summary>
        public const double DefaultSnapWindowSec = 0.25;

        /// <summary>
        /// How far (seconds) something may be from a snap target and still snap to it. The timeline sets this
        /// from the zoom so the reach stays the same number of pixels.
        /// </summary>
        public double SnapWindowSec { get; set; } = DefaultSnapWindowSec;

        /// <summary>
        /// Spacing (seconds) of the snap grid, or null for none. The grid is the timeline's visible gridlines;
        /// it is what you snap to when no clip edge, marker or playhead is closer.
        /// </summary>
        public double? SnapGridSec { get; set; }

        /// <summary>The playhead position, offered as a snap target (set by the application).</summary>
        public double? SnapPlayheadSec { get; set; }

        /// <summary>Snaps a time and returns what it snapped to. See <see cref="SnapMagnetic"/>.</summary>
        public double SnapTimeMagnetic(double sec) => SnapMagnetic(sec).Time;

        /// <summary>
        /// Snaps to the frame grid and nearby targets, ignoring the clip with <paramref name="excludeClipId"/>
        /// (and its link group) and the marker with <paramref name="excludeMarkerId"/>. Used when dragging so
        /// something snaps to *other* things, not itself.
        /// </summary>
        public double SnapTimeMagnetic(double sec, string? excludeClipId, string? excludeMarkerId = null)
            => SnapMagnetic(sec, excludeClipId, excludeMarkerId).Time;

        /// <summary>
        /// Snapping with a reason. When magnetic snapping is on, the closest of these within
        /// <see cref="SnapWindowSec"/> wins: clip edges, markers (and range edges), the playhead, the start of
        /// the timeline; failing those, the nearest grid line; failing that, the nearest frame.
        /// </summary>
        public SnapResult SnapMagnetic(double sec, string? excludeClipId = null, string? excludeMarkerId = null,
            bool includePlayhead = true, IReadOnlySet<string>? excludeClips = null)
        {
            var frame = SnapTime(sec);
            if (!MagneticSnap)
                return new SnapResult(frame, SnapKind.Frame);

            var excludeGroup = excludeClipId is null ? null : FindClip(excludeClipId)?.LinkGroupId;

            double? best = null;
            var bestKind = SnapKind.Frame;
            var bestDelta = double.MaxValue;

            foreach (var track in Document.Tracks)
                foreach (var clip in track.Clips)
                {
                    if (clip.Id == excludeClipId || (excludeGroup is not null && clip.LinkGroupId == excludeGroup)
                        || excludeClips?.Contains(clip.Id) == true)
                        continue;
                    Consider(clip.StartSec, SnapKind.ClipEdge);
                    Consider(clip.StartSec + clip.DurSec, SnapKind.ClipEdge);
                }

            foreach (var marker in Document.Markers)
            {
                if (marker.Id == excludeMarkerId)
                    continue;
                Consider(marker.StartSec, SnapKind.Marker);
                if (marker.IsRange)
                    Consider(marker.EndSec, SnapKind.Marker);
            }

            if (includePlayhead && SnapPlayheadSec is { } playhead)
                Consider(playhead, SnapKind.Playhead);
            // the start of the timeline is something to catch on, but only once there is something to line up with
            if (Document.Tracks.Any(t => t.Clips.Count > 0))
                Consider(0, SnapKind.ClipEdge);

            if (best is { } hit)
                return new SnapResult(hit, bestKind);

            if (SnapGridSec is { } grid && grid > 1e-6)
            {
                var line = Math.Round(sec / grid) * grid;
                if (Math.Abs(line - sec) <= SnapWindowSec)
                    return new SnapResult(SnapTime(line), SnapKind.Grid);
            }

            return new SnapResult(frame, SnapKind.Frame);

            void Consider(double target, SnapKind kind)
            {
                var delta = Math.Abs(target - sec);
                if (delta <= SnapWindowSec && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = target;
                    bestKind = kind;
                }
            }
        }
    }
}
