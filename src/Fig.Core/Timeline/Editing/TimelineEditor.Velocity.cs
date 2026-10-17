using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>A clip that can take a speed curve: a video clip (its own sound follows the curve).</summary>
        public bool CanHaveSpeedCurve(string clipId) => FindClip(clipId) is VideoClip { HasSourceRange: true };

        /// <summary>
        /// Gives the clip a speed curve: a flat line at its speed, a point at each end, so nothing changes until a
        /// point moves. One undo step.
        /// </summary>
        public void AddSpeedCurve(string clipId)
        {
            if (!CanHaveSpeedCurve(clipId) || FindClip(clipId) is not { } clip || ClipTime.HasCurve(clip))
                return;
            var speed = ParamValue.OfDouble(System.Math.Clamp(clip.Speed, ClipTime.MinSpeed, ClipTime.MaxSpeed));
            SetSpeedCurve(clipId, [new KeyframePoint(0, speed), new KeyframePoint(clip.DurSec, speed)], "Add speed curve");
        }

        /// <summary>Takes the curve off: the clip plays at one speed again, keeping its length. One undo step.</summary>
        public void RemoveSpeedCurve(string clipId)
        {
            if (FindClip(clipId) is { } clip && ClipTime.HasCurve(clip))
                SetSpeedCurve(clipId, null, "Remove speed curve");
        }

        /// <summary>
        /// Replaces the clip's speed curve; speeds are held to 0.1×–8× and times to 0 or later. Edits sharing a
        /// <paramref name="gesture"/> (one drag) are one undo step. The clip keeps its place and length.
        /// </summary>
        public void SetSpeedCurve(string clipId, IReadOnlyList<KeyframePoint>? keys, string description, string gesture = "")
        {
            if (FindClip(clipId) is not { } clip)
                return;
            var cleaned = keys?
                .Select(k => k with
                {
                    // points past the end stay: trimming the clip longer again brings them back
                    TimeSec = System.Math.Max(0, k.TimeSec),
                    Value = ParamValue.OfDouble(System.Math.Clamp(k.Value.AsNumber, ClipTime.MinSpeed, ClipTime.MaxSpeed)),
                })
                .GroupBy(k => System.Math.Round(k.TimeSec, 6)).Select(g => g.Last())
                .OrderBy(k => k.TimeSec).ToList();
            ReplaceKeyframes(KeyframeHostRef.OfSpeed(clipId),
                cleaned is { Count: > 0 } ? new Dictionary<string, List<KeyframePoint>> { [AutomationKeys.Speed] = cleaned } : null,
                description, gesture);
        }
    }
}
