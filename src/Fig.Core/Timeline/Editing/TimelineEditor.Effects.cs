using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Effects on clips and tracks: adding, removing, their bars, parameters and keyframes.
    /// </summary>
    public partial class TimelineEditor
    {
        public void AddEffect(string clipId, EffectInstance effect)
        {
            if (FindEffectOwner(clipId) is null)
                return;
            History.Execute(new AddEffectCommand(this, clipId, effect));
            RaiseChanged();
        }

        /// <summary>
        /// Limits an effect to part of the clip (or, with null start and length, puts it back on the whole
        /// clip). Moving a bar can carry its keyframes; resizing its edges leaves keyframe times unchanged.
        /// </summary>
        public void SetEffectRange(string clipId, string effectId, double? startSec, double? durSec, bool moveKeyframes = false)
        {
            var clip = FindEffectOwner(clipId);
            if (clip is null)
                return;
            if (startSec is { } s && durSec is { } d)
            {
                const double minLength = 0.1;
                d = Math.Clamp(d, Math.Min(minLength, clip.DurSec), EffectTimeLimit(clip));
                s = Math.Clamp(s, 0, Math.Max(0, EffectTimeLimit(clip) - d));
                startSec = s;
                durSec = d;
            }

            ExecuteEffectEdit(clipId, effectId, "Change effect range", $"range|{clipId}|{effectId}", fx =>
            {
                if (moveKeyframes && startSec is { } newStart)
                {
                    var delta = newStart - (fx.StartSec ?? 0);
                    foreach (var key in fx.Keyframes.Keys.ToArray())
                        fx.Keyframes[key] = fx.Keyframes[key].Select(point => point with
                        {
                            TimeSec = Math.Clamp(point.TimeSec + delta, 0, EffectTimeLimit(clip)),
                        }).ToList();
                }
                fx.StartSec = startSec;
                fx.DurSec = durSec;
            });
        }

        private void ExecuteEffectEdit(string clipId, string effectId, string description, string? coalesceKey,
            Action<EffectInstance> mutate)
        {
            if (FindEffectOwner(clipId)?.Effects.Any(e => e.Id == effectId) != true)
                return;
            var command = new EffectEditCommand(this, clipId, effectId, description, coalesceKey, mutate);
            if (coalesceKey is null)
                History.Execute(command);
            else
                History.ExecuteCoalescing(command);
            RaiseChanged();
        }

        /// <summary>
        /// Sets (or adds) the keyframe of an effect at a clip-relative time for every given parameter at once.
        /// Dragging a value coalesces into one undo step.
        /// </summary>
        public void SetEffectKeyframe(string clipId, string effectId, double timeSec,
            IReadOnlyDictionary<string, double> values, KeyframeEasing? easing = null)
            => EditKeyframes(KeyframeHostRef.OfEffect(clipId, effectId), "Set keyframe", $"kf|{clipId}|{effectId}|{Math.Round(timeSec, 4)}",
                host => Upsert(host, timeSec, values.ToDictionary(kv => kv.Key, kv => host.ToValue(kv.Key, kv.Value)), easing, host.NewKeyEasing));

        /// <summary>
        /// Adds a keyframe at a clip-relative time holding the effect's current look there, so adding it
        /// changes nothing visible. Keyframes hold their first and last values outside their range, so a
        /// single keyframe is already a valid (constant) animation. Returns the time keyed.
        /// </summary>
        public double? AddEffectKeyframe(string clipId, string effectId, double localSec)
            => AddKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), localSec);

        /// <summary>
        /// Turns animation on or off for an effect. On: a keyframe at the given time holds the current look.
        /// Off: every keyframe is removed and the look at that time becomes the constant value. This is the
        /// explicit "clear animation" edit; the stopwatch records with <see cref="RecordEffectKeyframe"/> instead.
        /// </summary>
        public void SetEffectAnimated(string clipId, string effectId, bool animated, double localSec)
        {
            var clip = FindEffectOwner(clipId);
            if (clip is null)
                return;
            var time = Math.Clamp(localSec, 0, EffectTimeLimit(clip));
            ExecuteEffectEdit(clipId, effectId, animated ? "Animate effect" : "Stop animating effect", null, fx =>
            {
                if (animated)
                {
                    if (fx.Keyframes.Count == 0)
                        UpsertKeyframes(fx, time, CurrentNumericValues(fx, time), null);
                    return;
                }
                var frozen = CurrentNumericValues(fx, time);
                fx.Keyframes.Clear();
                var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema ?? Array.Empty<ParamDef>();
                foreach (var (key, number) in frozen)
                {
                    var kind = schema.FirstOrDefault(p => p.Key == key)?.Kind ?? ParamKind.Double;
                    fx.Params[key] = kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
                }
            });
        }

        /// <summary>
        /// Records a keyframe at a clip-relative time holding the effect's current look there (the stopwatch).
        /// Recording only ever adds: an effect that is not animated yet starts with this keyframe, one that is
        /// keeps every keyframe it has, and a time that already has a keyframe is left as it is. Returns the
        /// time keyed, or null when the effect does not exist. Removing keyframes is a separate, explicit edit
        /// (<see cref="RemoveEffectKeyframe"/>, <see cref="SetEffectAnimated"/>).
        /// </summary>
        public double? RecordEffectKeyframe(string clipId, string effectId, double localSec)
            => RecordKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), localSec);

        /// <summary>Copies the keyframe at one time to another time (same values and easing).</summary>
        public void DuplicateEffectKeyframe(string clipId, string effectId, double fromSec, double toSec)
            => DuplicateKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), fromSec), toSec);

        private static Dictionary<string, double> CurrentNumericValues(EffectInstance fx, double localT)
        {
            var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema ?? Array.Empty<ParamDef>();
            var resolved = Fig.Core.Media.EffectPipeline.ResolveParams(fx, localT);
            var values = new Dictionary<string, double>();
            foreach (var def in schema)
                if (def.Kind is ParamKind.Double or ParamKind.Int && resolved.TryGetValue(def.Key, out var v))
                    values[def.Key] = v.AsNumber;
            return values;
        }

        private static void UpsertKeyframes(EffectInstance fx, double timeSec,
            IReadOnlyDictionary<string, double> values, KeyframeEasing? easing)
        {
            var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema;
            foreach (var (key, number) in values)
            {
                var kind = schema?.FirstOrDefault(p => p.Key == key)?.Kind ?? ParamKind.Double;
                if (kind is not (ParamKind.Double or ParamKind.Int))
                    continue;
                var value = kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
                if (!fx.Keyframes.TryGetValue(key, out var track))
                    fx.Keyframes[key] = track = new List<KeyframePoint>();
                var i = track.FindIndex(p => Math.Abs(p.TimeSec - timeSec) < 1e-6);
                if (i >= 0)
                    track[i] = new KeyframePoint(timeSec, value, easing ?? track[i].Easing);
                else
                {
                    track.Add(new KeyframePoint(timeSec, value, easing ?? KeyframeEasing.EaseInOut));
                    track.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
                }
            }
        }

        /// <summary>Removes the keyframe at a clip-relative time from every parameter of an effect.</summary>
        public void RemoveEffectKeyframe(string clipId, string effectId, double timeSec)
            => RemoveKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), timeSec));

        /// <summary>
        /// Moves a keyframe to a new time on every parameter of an effect. Pass the same
        /// <paramref name="gesture"/> for every step of one drag so it undoes in one go.
        /// </summary>
        public void MoveEffectKeyframe(string clipId, string effectId, double fromSec, double toSec, string gesture)
            => MoveKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), fromSec), toSec, gesture);

        /// <summary>Changes how the effect eases from the keyframe at a time to the next one.</summary>
        public void SetEffectKeyframeEasing(string clipId, string effectId, double timeSec, KeyframeEasing easing)
            => SetKeyframeEasing(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), timeSec), easing);

        public void RemoveEffect(string clipId, string effectId)
        {
            if (FindEffectOwner(clipId) is null)
                return;
            History.Execute(new RemoveEffectCommand(this, clipId, effectId));
            RaiseChanged();
        }

        /// <summary>Sets a typed parameter on an effect (slider drags coalesce into one undo step).</summary>
        public void SetEffectParam(string clipId, string effectId, string key, ParamValue value)
        {
            var effect = FindEffectOwner(clipId)?.Effects.FirstOrDefault(e => e.Id == effectId);
            if (effect is null || !effect.Params.TryGetValue(key, out var old) || old == value)
                return;
            ExecuteEffectEdit(clipId, effectId, $"Set effect param {key}", $"param|{key}", fx => fx.Params[key] = value);
        }

        public void ToggleEffect(string clipId, string effectId)
            => ExecuteEffectEdit(clipId, effectId, "Toggle effect", null, fx => fx.Enabled = !fx.Enabled);

        // ---- effect keyframes ----

        public void SetKeyframe(string clipId, string effectId, string key, double timeSec, ParamValue value)
            => SetKeyframeValue(KeyframeHostRef.OfEffect(clipId, effectId), key, timeSec, _ => value, null);

        public void RemoveKeyframe(string clipId, string effectId, string key, double timeSec)
            => RemoveKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), key, timeSec);

        public void ClearKeyframes(string clipId, string effectId, string key)
            => ClearKeyframes(KeyframeHostRef.OfEffect(clipId, effectId), key);
    }
}
