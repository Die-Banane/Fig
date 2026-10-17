using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Every keyframe edit, for every kind of keyframe: a clip's own (opacity, volume, crop, text size...), an
    /// effect's, and a clip's speed curve. Each names its keys with a <see cref="KeyframeHostRef"/> and goes through
    /// one <see cref="KeyframeEditCommand"/>, so moving, copying, easing and deleting work the same everywhere.
    /// Times are seconds from the host's start (<see cref="KeyframeHost.OriginSec"/>).
    /// </summary>
    public partial class TimelineEditor
    {
        /// <summary>The keyframes a reference names, with what is particular about where they live; null when gone.</summary>
        public KeyframeHost? FindKeyframeHost(KeyframeHostRef host)
        {
            if (host.EffectId is { } effectId)
            {
                var owner = FindEffectOwner(host.OwnerId);
                var fx = owner?.Effects.FirstOrDefault(e => e.Id == effectId);
                if (owner is null || fx is null)
                    return null;
                var schema = EffectCatalog.Find(fx.TypeId)?.ParamSchema;
                return new KeyframeHost(host, fx.Keyframes, _ => true,
                    owner is Track ? 0 : owner.StartSec, EffectTimeLimit(owner), KeyframeEasing.EaseInOut,
                    (key, v) => schema?.FirstOrDefault(p => p.Key == key)?.Kind == ParamKind.Int
                        ? ParamValue.OfInt((int)Math.Round(v)) : ParamValue.OfDouble(v),
                    t => CurrentNumericValues(fx, t),
                    () => { });
            }

            if (FindClip(host.OwnerId) is not { } clip)
                return null;
            if (host.Lane == KeyframeHostRef.SpeedLane)
                return new KeyframeHost(host, clip.Keyframes, k => k == AutomationKeys.Speed,
                    clip.StartSec, clip.DurSec, KeyframeEasing.Linear,
                    (_, v) => ParamValue.OfDouble(Math.Clamp(v, ClipTime.MinSpeed, ClipTime.MaxSpeed)),
                    t => new Dictionary<string, double> { [AutomationKeys.Speed] = ClipTime.SpeedAt(clip, t) },
                    () => SyncSpeedRange(clip));
            return new KeyframeHost(host, clip.Keyframes, k => k != AutomationKeys.Speed,
                clip.StartSec, clip.DurSec, KeyframeEasing.Linear,
                (_, v) => ParamValue.OfDouble(v),
                t => clip.Keyframes.Where(kv => kv.Key != AutomationKeys.Speed && kv.Value.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => ClipAutomation.Evaluate(kv.Value, t)),
                () => { });
        }

        /// <summary>A speed curve changed: the clip's out point is wherever the curve (or its one speed) now leaves it.</summary>
        private static void SyncSpeedRange(Clip clip)
        {
            if (!clip.HasSourceRange)
                return;
            if (ClipTime.HasCurve(clip))
                ClipTime.SyncSourceOut(clip);
            else
                clip.SetSourceRange(clip.SourceIn, clip.SourceIn + clip.DurSec * (clip.Speed <= 0 ? 1 : clip.Speed));
        }

        /// <summary>Runs one keyframe edit as an undo step (folding into the last one when <paramref name="coalesceKey"/> matches).</summary>
        private void EditKeyframes(KeyframeHostRef host, string description, string? coalesceKey, Action<KeyframeHost> mutate)
        {
            if (FindKeyframeHost(host) is null)
                return;
            var command = new KeyframeEditCommand(this, host, description, coalesceKey, mutate);
            if (coalesceKey is null)
                History.Execute(command);
            else
                History.ExecuteCoalescing(command);
            RaiseChanged();
        }

        /// <summary>The key nearest a time in a list (within a millisecond), or -1.</summary>
        private static int KeyAt(List<KeyframePoint> track, double timeSec)
        {
            var best = -1;
            var bestDistance = 1e-3;
            for (var i = 0; i < track.Count; i++)
            {
                var distance = Math.Abs(track[i].TimeSec - timeSec);
                if (distance <= bestDistance)
                    (best, bestDistance) = (i, distance);
            }
            return best;
        }

        /// <summary>Moves a diamond (every key at its time) to <paramref name="toSec"/>. Steps of one drag share a <paramref name="gesture"/>.</summary>
        public void MoveKeyframes(KeyframeRef keyframe, double toSec, string gesture)
        {
            if (FindKeyframeHost(keyframe.Host) is not { } found)
                return;
            toSec = Math.Clamp(toSec, 0, found.LimitSec);
            EditKeyframes(keyframe.Host, "Move keyframe", $"drag|{gesture}", host =>
            {
                foreach (var (_, track) in host.Tracks)
                {
                    var i = KeyAt(track, keyframe.TimeSec);
                    if (i < 0)
                        continue;
                    var point = track[i];
                    track.RemoveAt(i);
                    track.RemoveAll(p => Math.Abs(p.TimeSec - toSec) < 1e-6);
                    track.Add(point with { TimeSec = toSec });
                }
            });
        }

        /// <summary>Copies a diamond (same values and easing) to another time.</summary>
        public void DuplicateKeyframes(KeyframeRef keyframe, double toSec)
        {
            if (FindKeyframeHost(keyframe.Host) is not { } found)
                return;
            toSec = Math.Clamp(toSec, 0, found.LimitSec);
            EditKeyframes(keyframe.Host, "Duplicate keyframe", null, host =>
            {
                foreach (var (_, track) in host.Tracks)
                {
                    var i = KeyAt(track, keyframe.TimeSec);
                    if (i < 0)
                        continue;
                    var copy = track[i] with { TimeSec = toSec };
                    track.RemoveAll(p => Math.Abs(p.TimeSec - toSec) < 1e-6);
                    track.Add(copy);
                }
            });
        }

        /// <summary>Deletes a diamond: every key at its time.</summary>
        public void RemoveKeyframes(KeyframeRef keyframe)
            => EditKeyframes(keyframe.Host, "Delete keyframe", null, host =>
            {
                foreach (var (_, track) in host.Tracks)
                    if (KeyAt(track, keyframe.TimeSec) is var i and >= 0)
                        track.RemoveAt(i);
            });

        /// <summary>How the diamond's keys move on to the next ones.</summary>
        public void SetKeyframeEasing(KeyframeRef keyframe, KeyframeEasing easing)
            => EditKeyframes(keyframe.Host, "Change easing", null, host =>
            {
                foreach (var (_, track) in host.Tracks)
                    if (KeyAt(track, keyframe.TimeSec) is var i and >= 0)
                        track[i] = track[i] with { Easing = easing };
            });

        /// <summary>
        /// Adds a key at a time holding the host's current look there, so adding it changes nothing visible (keys hold
        /// their first and last values outside their range). Returns the time keyed, or null when there is nothing to key.
        /// </summary>
        public double? AddKeyframe(KeyframeHostRef hostRef, double timeSec, string description = "Add keyframe")
        {
            if (FindKeyframeHost(hostRef) is not { } found)
                return null;
            var time = Math.Clamp(timeSec, 0, found.LimitSec);
            var values = found.CurrentValues(time);
            if (values.Count == 0)
                return null;
            EditKeyframes(hostRef, description, null,
                host => Upsert(host, time, values.ToDictionary(kv => kv.Key, kv => host.ToValue(kv.Key, kv.Value)), null, host.NewKeyEasing));
            return time;
        }

        /// <summary>The stopwatch: keys the current look at a time, unless there already is a key there. Never removes any.</summary>
        public double? RecordKeyframe(KeyframeHostRef hostRef, double timeSec)
        {
            if (FindKeyframeHost(hostRef) is not { } found)
                return null;
            var time = Math.Clamp(timeSec, 0, found.LimitSec);
            if (found.HasKeyAt(time))
                return time;
            return AddKeyframe(hostRef, time, found.Tracks.Any() ? "Record keyframe" : "Animate");
        }

        /// <summary>
        /// Sets one key's value at a time: updates the key there (keeping its easing), or adds one. Edits sharing a
        /// <paramref name="gesture"/> (a drag of the value) are one undo step.
        /// </summary>
        public void SetKeyframeValue(KeyframeHostRef hostRef, string key, double timeSec, double value, string? gesture = null)
            => SetKeyframeValue(hostRef, key, timeSec, (KeyframeHost host) => host.ToValue(key, value), gesture);

        /// <summary>Sets one key to a typed value (a colour, a choice...) at a time, as <see cref="SetKeyframeValue(KeyframeHostRef, string, double, double, string?)"/>.</summary>
        public void SetKeyframe(KeyframeHostRef hostRef, string key, double timeSec, ParamValue value, string? gesture = null)
            => SetKeyframeValue(hostRef, key, timeSec, _ => value, gesture);

        private void SetKeyframeValue(KeyframeHostRef hostRef, string key, double timeSec, Func<KeyframeHost, ParamValue> value,
            string? gesture, KeyframeEasing newKeyEasing = KeyframeEasing.Linear)
        {
            if (FindKeyframeHost(hostRef) is not { } found || !found.Includes(key))
                return;
            var time = Math.Clamp(timeSec, 0, found.LimitSec);
            EditKeyframes(hostRef, "Set keyframe", gesture is null ? null : $"drag|{gesture}",
                host => Upsert(host, time, new Dictionary<string, ParamValue> { [key] = value(host) }, null, newKeyEasing));
        }

        /// <summary>Removes the key of one property at a time.</summary>
        public void RemoveKeyframe(KeyframeHostRef hostRef, string key, double timeSec)
            => EditKeyframes(hostRef, "Remove keyframe", null, host =>
            {
                if (host.Includes(key) && host.Store.TryGetValue(key, out var track) && KeyAt(track, timeSec) is var i and >= 0)
                    track.RemoveAt(i);
            });

        /// <summary>Removes every key of one property, or every key the host has when <paramref name="key"/> is null.</summary>
        public void ClearKeyframes(KeyframeHostRef hostRef, string? key)
            => EditKeyframes(hostRef, "Clear keyframes", null, host =>
            {
                foreach (var (name, track) in host.Tracks.ToList())
                    if (key is null || name == key)
                        track.Clear();
            });

        /// <summary>Replaces all of a host's keys (null or none removes them), e.g. a whole speed curve.</summary>
        public void ReplaceKeyframes(KeyframeHostRef hostRef, IReadOnlyDictionary<string, List<KeyframePoint>>? keys,
            string description, string gesture = "")
            => EditKeyframes(hostRef, description, gesture.Length > 0 ? $"replace|{gesture}" : null, host =>
            {
                foreach (var (_, track) in host.Tracks.ToList())
                    track.Clear();
                if (keys is null)
                    return;
                foreach (var (name, track) in keys)
                    if (host.Includes(name) && track.Count > 0)
                        host.Store[name] = track.ToList();
            });

        /// <summary>
        /// Sets a clip property on clips (one undo step; a slider drag folds into it). An animated property sets the
        /// key at the playhead instead (each clip's own moment), so dragging a keyed slider reshapes the animation there.
        /// </summary>
        public void SetClipProperty(IEnumerable<string> clipIds, ClipProperty property, ParamValue value, double playheadSec)
        {
            var clips = clipIds.Distinct().Select(FindClip).Where(c => c is not null && property.Has(c)).Cast<Clip>().ToList();
            if (clips.Count == 0)
                return;
            var animated = clips.Where(property.IsAnimated).ToList();
            foreach (var clip in animated)
            {
                var local = Math.Clamp(playheadSec - clip.StartSec, 0, clip.DurSec);
                SetKeyframeValue(KeyframeHostRef.OfClip(clip.Id), property.Key, local, value.AsNumber, $"{property.Key}@{Math.Round(local, 4)}");
            }
            var still = clips.Except(animated).Select(c => c.Id).ToList();
            if (still.Count > 0)
                SetClipValue(still, property.Key, property.Get, property.Set, value);
        }

        /// <summary>
        /// Writes keys at a time: a key already there takes the new value and keeps its easing (or takes
        /// <paramref name="easing"/>), a new one eases <paramref name="easing"/> or <paramref name="newKeyEasing"/>.
        /// </summary>
        private static void Upsert(KeyframeHost host, double timeSec, IReadOnlyDictionary<string, ParamValue> values,
            KeyframeEasing? easing, KeyframeEasing newKeyEasing)
        {
            foreach (var (key, value) in values)
            {
                if (!host.Includes(key))
                    continue;
                if (!host.Store.TryGetValue(key, out var track))
                    host.Store[key] = track = new List<KeyframePoint>();
                var i = track.FindIndex(p => Math.Abs(p.TimeSec - timeSec) < 1e-6);
                if (i >= 0)
                    track[i] = new KeyframePoint(timeSec, value, easing ?? track[i].Easing);
                else
                    track.Add(new KeyframePoint(timeSec, value, easing ?? newKeyEasing));
            }
        }
    }
}
