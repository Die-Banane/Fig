using System;
using System.Collections.Generic;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>One keyframe of a preset: where it falls along the effect's bar (0..1) and its values.</summary>
    public sealed class PresetKeyframe
    {
        /// <summary>0 = start of the bar, 1 = end.</summary>
        public double At { get; set; }

        /// <summary>How the values move from this keyframe to the next.</summary>
        public KeyframeEasing Easing { get; set; } = KeyframeEasing.EaseInOut;

        public Dictionary<string, double> Params { get; set; } = new();
    }

    /// <summary>
    /// A named, reusable bundle of an effect and its keyframes. "Zoom in", "Pan" and friends are
    /// presets of the Transform effect: pure data, so new ones (built in or saved by the user) need
    /// no code.
    /// </summary>
    public sealed class EffectPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "focus";

        /// <summary>The effect the preset creates.</summary>
        public string TypeId { get; set; } = EffectCatalog.Transform;

        public double DefaultDurationSec { get; set; } = 3;
        public bool BuiltIn { get; set; }
        public List<PresetKeyframe> Keyframes { get; set; } = new();
    }
}
