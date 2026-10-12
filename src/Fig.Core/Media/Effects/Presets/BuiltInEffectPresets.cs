using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>The presets that ship with Fig. All of them are plain Transform keyframes.</summary>
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
        };

        public static EffectPreset? Find(string id)
        {
            foreach (var preset in All)
                if (preset.Id == id)
                    return preset;
            return null;
        }

        private static PresetKeyframe Key(double at, double zoom, double centerX = 0.5, double centerY = 0.5)
            => new()
            {
                At = at,
                Easing = KeyframeEasing.EaseInOut,
                Params = new Dictionary<string, double>
                {
                    ["zoom"] = zoom,
                    ["centerX"] = centerX,
                    ["centerY"] = centerY,
                    ["rotation"] = 0,
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
