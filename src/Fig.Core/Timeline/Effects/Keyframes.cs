using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    /// <summary>How a value moves from a keyframe to the next one.</summary>
    public enum KeyframeEasing
    {
        /// <summary>Constant speed.</summary>
        Linear,
        /// <summary>Starts slowly.</summary>
        EaseIn,
        /// <summary>Ends slowly.</summary>
        EaseOut,
        /// <summary>Starts and ends slowly.</summary>
        EaseInOut,
        /// <summary>Stays put until the next keyframe, then jumps.</summary>
        Hold,
    }

    public static class KeyframeEasingMath
    {
        /// <summary>Maps linear progress 0..1 through an easing curve (0..1 in, 0..1 out).</summary>
        public static double Apply(KeyframeEasing easing, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return easing switch
            {
                KeyframeEasing.EaseIn => t * t,
                KeyframeEasing.EaseOut => 1 - (1 - t) * (1 - t),
                KeyframeEasing.EaseInOut => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
                KeyframeEasing.Hold => t >= 1 ? 1 : 0,
                _ => t,
            };
        }
    }

    /// <summary>
    /// One keyframe in its owner's time domain: absolute timeline seconds for track effects, local seconds for legacy clip effects.
    /// <see cref="Easing"/> shapes the movement from this keyframe to the next.
    /// </summary>
    public readonly record struct KeyframePoint(double TimeSec, ParamValue Value, KeyframeEasing Easing = KeyframeEasing.Linear);
}
