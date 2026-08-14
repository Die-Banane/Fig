using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Finds abutting video clips with matching edge transitions and reports progress
    /// across the centered window [cut - D/2, cut + D/2), where D is the total duration.
    /// </summary>
    public static class TransitionResolver
    {
        private const double AbutEps = 1e-3;

        public static ActiveTransition? FindActive(TimelineModel timeline, double timeSec)
        {
            foreach (var track in timeline.Tracks)
            {
                if (track.Kind != TrackKind.Video || !track.Visible)
                    continue;
                if (FindActive(track, timeSec) is { } active)
                    return active;
            }
            return null;
        }

        /// <summary>Finds the active transition on one video track.</summary>
        public static ActiveTransition? FindActive(Track track, double timeSec)
        {
            if (track.Kind != TrackKind.Video || !track.Visible)
                return null;

            // Avoid allocating and sorting a fresh list for every rendered frame. Clip storage
            // order is not stable after moves/imports, so find the abutting partner directly.
            var clips = track.Clips;
            for (var i = 0; i < clips.Count; i++)
            {
                var a = clips[i];
                if (!a.Enabled)
                    continue;
                var cut = a.StartSec + a.DurSec;
                for (var j = 0; j < clips.Count; j++)
                {
                    if (i == j)
                        continue;
                    var b = clips[j];
                    if (!b.Enabled || Math.Abs(b.StartSec - cut) > AbutEps)
                        continue;

                    var tx = ResolvePair(a, b);
                    if (tx is null || tx.DurationSec <= 1e-6)
                        continue;

                    var d = tx.DurationSec;
                    var winStart = cut - d / 2;
                    var winEnd = cut + d / 2;
                    if (timeSec < winStart || timeSec >= winEnd)
                        continue;

                    return new ActiveTransition
                    {
                        Outgoing = a,
                        Incoming = b,
                        TypeId = tx.TypeId,
                        Progress01 = Math.Clamp((timeSec - winStart) / d, 0, 1),
                        DurationSec = d,
                        Params = tx.Params,
                        CutSec = cut,
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// Prefer a shared type when both edges declare one; otherwise use whichever is set.
        /// Duration is the max of the two (capped later by media handles if needed).
        /// </summary>
        private static TransitionRef? ResolvePair(Clip a, Clip b)
        {
            var outTx = a.TransitionOut;
            var inTx = b.TransitionIn;
            if (outTx is null && inTx is null)
                return null;

            if (outTx is not null && inTx is not null)
            {
                if (outTx.TypeId != inTx.TypeId)
                    return outTx; // outgoing wins on type mismatch
                return new TransitionRef
                {
                    TypeId = outTx.TypeId,
                    DurationSec = Math.Max(outTx.DurationSec, inTx.DurationSec),
                    Params = new Dictionary<string, ParamValue>(outTx.Params),
                };
            }

            return outTx ?? inTx;
        }

        /// <summary>True when a normal covering clip should be omitted because a transition owns this time.</summary>
        public static bool SuppressNormalLayer(ActiveTransition? active, Clip clip, double timeSec)
        {
            if (active is null)
                return false;
            // During the transition window, both A and B are handled by the blender — skip normal layers.
            return ReferenceEquals(clip, active.Outgoing) || ReferenceEquals(clip, active.Incoming)
                   || clip.Id == active.Outgoing.Id || clip.Id == active.Incoming.Id;
        }
    }
}
