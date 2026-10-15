using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    /// <summary>Optional transition attached to a clip edge (library / between-clip path).</summary>
    public sealed class TransitionRef
    {
        public string TypeId { get; set; } = "";
        /// <summary>Total transition duration, centered equally around the cut.</summary>
        public double DurationSec { get; set; } = 0.5;
        public Dictionary<string, ParamValue> Params { get; set; } = new();

        public TransitionRef Clone()
        {
            return new TransitionRef
            {
                TypeId = TypeId,
                DurationSec = DurationSec,
                Params = new Dictionary<string, ParamValue>(Params),
            };
        }
    }
}
