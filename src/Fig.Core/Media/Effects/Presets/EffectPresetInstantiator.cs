using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>Turns presets into effects on a clip, and effects back into presets.</summary>
    public static class EffectPresetInstantiator
    {
        /// <summary>
        /// Builds the effect a preset describes, with its keyframes starting at <paramref name="atLocalSec"/> on a
        /// clip <paramref name="clipDurSec"/> long (the motion is shortened and shifted back to fit). The effect covers
        /// the whole clip: its first and last keyframes hold their values before and after the motion.
        /// </summary>
        public static EffectInstance Create(EffectPreset preset, double clipDurSec, double atLocalSec)
        {
            var entry = EffectCatalog.Find(preset.TypeId)
                ?? throw new InvalidOperationException($"Unknown effect '{preset.TypeId}' in preset '{preset.Name}'");

            var instance = entry.CreateInstance();
            var duration = Math.Min(Math.Max(0.1, preset.DefaultDurationSec), Math.Max(0.1, clipDurSec));
            var start = Math.Clamp(atLocalSec, 0, Math.Max(0, clipDurSec - duration));

            var keys = preset.Keyframes.OrderBy(k => k.At).ToList();
            foreach (var key in keys)
            {
                var time = start + Math.Clamp(key.At, 0, 1) * duration;
                foreach (var (name, number) in key.Params)
                {
                    if (!(entry.ParamSchema.FirstOrDefault(p => p.Key == name) is { } def) || !IsNumeric(def.Kind))
                        continue;
                    var value = def.Kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
                    if (!instance.Keyframes.TryGetValue(name, out var track))
                        instance.Keyframes[name] = track = new List<KeyframePoint>();
                    track.Add(new KeyframePoint(time, value, key.Easing));
                }
            }

            // the plain parameter values are the starting look, used wherever nothing is keyframed
            if (keys.Count > 0)
                foreach (var (name, number) in keys[0].Params)
                    if (entry.ParamSchema.FirstOrDefault(p => p.Key == name) is { } def && IsNumeric(def.Kind))
                        instance.Params[name] = def.Kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
            return instance;
        }

        /// <summary>
        /// Captures an effect's keyframes as a preset (times become 0..1 from its first keyframe to its last).
        /// Only numeric parameters are captured.
        /// </summary>
        public static EffectPreset FromEffect(EffectInstance effect, double clipDurSec, string name)
        {
            var times = effect.Keyframes.Values.SelectMany(t => t.Select(k => k.TimeSec))
                .Select(t => Math.Round(t, 6)).Distinct().OrderBy(t => t).ToList();
            if (times.Count == 0)
                times = new List<double> { 0 };
            var start = times[0];
            var duration = Math.Max(0.1, times[^1] - start);

            var preset = new EffectPreset
            {
                Name = name,
                TypeId = effect.TypeId,
                Icon = EffectCatalog.Find(effect.TypeId)?.Icon ?? "wand-sparkles",
                DefaultDurationSec = duration,
            };
            foreach (var time in times)
            {
                var resolved = EffectPipeline.ResolveParams(effect, time);
                var easing = effect.Keyframes.Values
                    .SelectMany(t => t).FirstOrDefault(k => Math.Abs(k.TimeSec - time) < 1e-6).Easing;
                var key = new PresetKeyframe
                {
                    At = duration <= 1e-9 ? 0 : Math.Clamp((time - start) / duration, 0, 1),
                    Easing = easing,
                };
                foreach (var (param, value) in resolved)
                    if (value.Kind is ParamKind.Double or ParamKind.Int)
                        key.Params[param] = value.AsNumber;
                preset.Keyframes.Add(key);
            }
            return preset;
        }

        private static bool IsNumeric(ParamKind kind) => kind is ParamKind.Double or ParamKind.Int;
    }
}
