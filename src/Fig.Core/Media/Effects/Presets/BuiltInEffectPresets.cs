using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>The presets that ship with Fig: reusable motion and short impact envelopes.</summary>
    public static class BuiltInEffectPresets
    {
        public static IReadOnlyList<EffectPreset> All { get; } = new[]
        {
            Make("zoom-in", "Zoom in", "Ease in on the middle of the picture.", "zoom-in", 3,
                Key(0, 1), Key(1, 2)),
            Make("zoom-out", "Zoom out", "Start close and ease back out to the full picture.", "zoom-out", 3,
                Key(0, 2), Key(1, 1)),
            Make("zoom-in-out", "Zoom in and out", "Zoom in, hold, and zoom back out smoothly.", "focus", 4,
                Key(0, 1), Key(0.3, 2), Key(0.7, 2), Key(1, 1)),
            Make("pan", "Pan", "Slide across the picture while slightly zoomed in.", "move-horizontal", 4,
                Key(0, 1.5, 0.35, 0.5), Key(1, 1.5, 0.65, 0.5)),
            Make("ken-burns", "Slow push", "A gentle, slow drift toward one side.", "move", 6,
                Key(0, 1, 0.5, 0.5), Key(1, 1.25, 0.58, 0.46)),
            Make("punch-zoom", "Punch Zoom", "A quick push and smooth recovery for a musical hit.", "zoom-in", 0.45,
                Key(0, 1), Key(0.15, 1.3), Key(1, 1)),
            Make("tilt-bounce", "Tilt Bounce", "Rock the camera through a short zoomed impact.", "rotate-cw", 0.6,
                // Zoom stays above cos(r) + (16/9) * sin(r) along the whole move so rotated edges never show.
                Key(0, 1), Key(0.2, 1.24, rotation: 7), Key(0.5, 1.12, rotation: -3), Key(1, 1)),
            Make("whip-left", "Whip Left", "A fast lateral camera move that settles back to center.", "move-horizontal", 0.5,
                Key(0, 1.15), Key(0.18, 1.4, 0.7), Key(1, 1.15)),
            Make("whip-right", "Whip Right", "A fast lateral camera move in the opposite direction.", "move-horizontal", 0.5,
                Key(0, 1.15), Key(0.18, 1.4, 0.3), Key(1, 1.15)),
            Impact("white-flash-hit", "White Flash Hit", "A brief exposure flash that settles to the original picture.", "brightness", 0.3, 0.9),
            Impact("glow-hit", "Glow Hit", "Bloom the highlights briefly on an impact.", "glow", 0.5, 1.8),
            Impact("glitch-burst", "Glitch Burst", "A short burst of digital slice distortion.", "digital-glitch", 0.4, 0.9),
            Impact("shake-hit", "Shake Hit", "A camera shake that quickly decays after a hit.", "camera-shake", 0.5, 0.065),
        };

        public static EffectPreset? Find(string id)
        {
            foreach (var preset in All)
                if (preset.Id == id)
                    return preset;
            return null;
        }

        private static PresetKeyframe Key(double at, double zoom, double centerX = 0.5, double centerY = 0.5, double rotation = 0)
            => new()
            {
                At = at,
                Easing = KeyframeEasing.EaseInOut,
                Params = new Dictionary<string, double>
                {
                    ["zoom"] = zoom,
                    ["centerX"] = centerX,
                    ["centerY"] = centerY,
                    ["rotation"] = rotation,
                },
            };

        private static EffectPreset Impact(string id, string name, string description, string typeId, double duration, double peak)
            => new()
            {
                Id = id, Name = name, Description = description, TypeId = typeId,
                Category = typeId == EffectCatalog.Brightness ? "Light" : null,
                Icon = "zap", DefaultDurationSec = duration, BuiltIn = true,
                Keyframes = new List<PresetKeyframe>
                {
                    new() { At = 0, Params = new() { ["amount"] = 0 } },
                    new() { At = 0.12, Params = new() { ["amount"] = peak } },
                    new() { At = 1, Params = new() { ["amount"] = 0 } },
                },
            };

        private static EffectPreset Make(string id, string name, string description, string icon,
            double duration, params PresetKeyframe[] keys)
            => new()
            {
                Id = id,
                Name = name,
                Description = description,
                Icon = icon,
                TypeId = EffectCatalog.Transform,
                DefaultDurationSec = duration,
                BuiltIn = true,
                Keyframes = new List<PresetKeyframe>(keys),
            };
    }
}
