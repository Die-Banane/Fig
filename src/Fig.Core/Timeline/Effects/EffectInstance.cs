using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    /// <summary>One effect in an ordered track or legacy clip filter stack.</summary>
    public sealed class EffectInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TypeId { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }

        /// <summary>
        /// Optional active range in the owner's time domain. Track ranges use absolute timeline seconds;
        /// legacy clip ranges use seconds from the clip start. Both null means the whole owner.
        /// Keyframes use the same time domain and hold their first and last values outside their range.
        /// </summary>
        public double? StartSec { get; set; }
        public double? DurSec { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasRange => StartSec is not null && DurSec is > 0;

        /// <summary>True when the effect is in force at a time in its owner's time domain.</summary>
        public bool IsActiveAt(double localT)
            => !HasRange || (localT >= StartSec!.Value - 1e-9 && localT <= StartSec.Value + DurSec!.Value + 1e-9);

        /// <summary>Typed parameters keyed by name (schema lives in the catalog).</summary>
        public Dictionary<string, ParamValue> Params { get; set; } = new();

        /// <summary>
        /// Optional keyframe tracks keyed by parameter name. When a track exists for a param,
        /// its value is evaluated at render time instead of the constant in <see cref="Params"/>.
        /// </summary>
        public Dictionary<string, List<KeyframePoint>> Keyframes { get; set; } = new();

        public EffectInstance Clone()
        {
            return new EffectInstance
            {
                Id = Guid.NewGuid().ToString(),
                TypeId = TypeId,
                Enabled = Enabled,
                Order = Order,
                StartSec = StartSec,
                DurSec = DurSec,
                Params = new Dictionary<string, ParamValue>(Params),
                Keyframes = CloneKeyframes(Keyframes),
            };
        }

        /// <summary>Clone preserving the same id (for undo snapshots / clip clone).</summary>
        public EffectInstance CloneKeepId()
        {
            return new EffectInstance
            {
                Id = Id,
                TypeId = TypeId,
                Enabled = Enabled,
                Order = Order,
                StartSec = StartSec,
                DurSec = DurSec,
                Params = new Dictionary<string, ParamValue>(Params),
                Keyframes = CloneKeyframes(Keyframes),
            };
        }

        private static Dictionary<string, List<KeyframePoint>> CloneKeyframes(Dictionary<string, List<KeyframePoint>> source)
        {
            var map = new Dictionary<string, List<KeyframePoint>>(source.Count);
            foreach (var (key, track) in source)
                map[key] = new List<KeyframePoint>(track);
            return map;
        }
    }
}
