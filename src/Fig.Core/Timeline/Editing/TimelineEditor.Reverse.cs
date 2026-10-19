using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>
        /// The clips that reversing <paramref name="clipIds"/> would change, linked partners included (a video
        /// and its sound reverse together). A clip with footage needs its reversed copy (or its original, to
        /// reverse it back) before <see cref="Reverse"/> can turn it round.
        /// </summary>
        public IReadOnlyList<Clip> ReverseTargets(IEnumerable<string> clipIds)
            => clipIds.SelectMany(LinkGroup).DistinctBy(c => c.Id).ToList();

        /// <summary>
        /// Plays clips backwards, as one undo step. A clip with footage (video, sound) switches to the other
        /// direction's copy of it, which <paramref name="counterpart"/> gives for its media (the reversed copy for
        /// an original, the original for a copy), its range mirrored so it shows the same stretch of footage the
        /// other way round; its keyframes stay where they are on the timeline. A clip without footage (text, a
        /// still) has its animation reversed: its keyframes are mirrored in time. Returns false when a clip with
        /// footage has no counterpart yet, changing nothing.
        /// </summary>
        public bool Reverse(IEnumerable<string> clipIds, Func<MediaAsset, MediaAsset?> counterpart)
        {
            var clips = ReverseTargets(clipIds);
            var plan = new List<(Clip Clip, MediaAsset? Other, double Length)>();
            foreach (var clip in clips)
            {
                if (clip.MediaId is not { } mediaId || clip is ImageClip)
                {
                    plan.Add((clip, null, 0));
                    continue;
                }
                if (MediaResolver?.Invoke(mediaId) is not { } asset || counterpart(asset) is not { } other)
                    return false;
                // the copy's length is the mirror line: time t in one is length - t in the other
                var length = asset.ReverseOf is not null ? asset.DurationSec : other.DurationSec;
                plan.Add((clip, other, length));
            }
            if (plan.Count == 0)
                return false;
            History.Execute(new ReverseClipsCommand(plan));
            RaiseChanged();
            return true;
        }
    }

    /// <summary>Turns clips round (see <see cref="TimelineEditor.Reverse"/>); undo puts their footage and keyframes back.</summary>
    public sealed class ReverseClipsCommand : IEditCommand
    {
        private readonly IReadOnlyList<(Clip Clip, MediaAsset? Other, double Length)> _plan;
        private readonly List<(Clip Clip, string? SourceId, double In, double Out, Dictionary<string, List<KeyframePoint>> Keys)> _before = new();

        public ReverseClipsCommand(IReadOnlyList<(Clip Clip, MediaAsset? Other, double Length)> plan) => _plan = plan;

        public string Description => "Reverse";

        public void Execute()
        {
            _before.Clear();
            foreach (var (clip, other, length) in _plan)
            {
                _before.Add((clip, SourceIdOf(clip), clip.SourceIn, clip.SourceOut, ClipTime.CopyKeyframes(clip)));
                if (other is null)
                {
                    MirrorKeyframes(clip);
                    continue;
                }
                var (srcIn, srcOut) = (clip.SourceIn, clip.SourceOut);
                SetSourceId(clip, other.Id);
                clip.SetSourceRange(Math.Max(0, length - srcOut), Math.Max(0, length - srcIn));
            }
        }

        public void Undo()
        {
            foreach (var (clip, sourceId, srcIn, srcOut, keys) in _before)
            {
                if (sourceId is not null)
                    SetSourceId(clip, sourceId);
                clip.SetSourceRange(srcIn, srcOut);
                clip.Keyframes = keys.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
            }
        }

        public void Redo() => Execute();

        private static string? SourceIdOf(Clip clip) => clip switch
        {
            VideoClip v => v.SourceId,
            AudioClip a => a.SourceId,
            _ => null,
        };

        private static void SetSourceId(Clip clip, string id)
        {
            if (clip is VideoClip v)
                v.SourceId = id;
            else if (clip is AudioClip a)
                a.SourceId = id;
        }

        /// <summary>
        /// Each animation run backwards: a key at t moves to (length - t), and the movement out of each key is the
        /// one that led into it before, its easing turned round (an ease in becomes an ease out).
        /// </summary>
        private static void MirrorKeyframes(Clip clip)
        {
            foreach (var key in clip.Keyframes.Keys.ToList())
            {
                var keys = clip.Keyframes[key];
                var mirrored = new List<KeyframePoint>(keys.Count);
                for (var i = keys.Count - 1; i >= 0; i--)
                {
                    // the segment that now leaves this key is the one that arrived at it: its easing was on the key before
                    var easing = i > 0 ? Turned(keys[i - 1].Easing) : KeyframeEasing.Linear;
                    mirrored.Add(new KeyframePoint(Math.Clamp(clip.DurSec - keys[i].TimeSec, 0, clip.DurSec), keys[i].Value, easing));
                }
                clip.Keyframes[key] = mirrored;
            }
        }

        private static KeyframeEasing Turned(KeyframeEasing easing) => easing switch
        {
            KeyframeEasing.EaseIn => KeyframeEasing.EaseOut,
            KeyframeEasing.EaseOut => KeyframeEasing.EaseIn,
            _ => easing,
        };
    }
}
