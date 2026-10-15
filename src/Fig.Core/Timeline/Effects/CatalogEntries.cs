using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    public enum EffectKind
    {
        Video,
        Audio,
        Both,
    }

    public sealed class EffectCatalogEntry
    {
        public string Category { get; init; } = "Stylize";
        public string TypeId { get; init; } = "";
        public EffectKind Kind { get; init; }
        public string DisplayName { get; init; } = "";
        public string Icon { get; init; } = "wand-sparkles";
        public string Description { get; init; } = "";

        /// <summary>True for effects the user built (graph effects) rather than ones that ship with Fig.</summary>
        public bool IsCustom { get; init; }

        public IReadOnlyList<ParamDef> ParamSchema { get; init; } = Array.Empty<ParamDef>();

        public Dictionary<string, ParamValue> DefaultParams()
        {
            var map = new Dictionary<string, ParamValue>();
            foreach (var p in ParamSchema)
                map[p.Key] = p.DefaultValue();
            return map;
        }

        public EffectInstance CreateInstance()
        {
            return new EffectInstance
            {
                TypeId = TypeId,
                Enabled = true,
                Params = DefaultParams(),
            };
        }
    }

    public sealed class TransitionCatalogEntry
    {
        public string Category { get; init; } = "Basic";
        public string TypeId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Icon { get; init; } = "blend";
        public string Description { get; init; } = "";
        public double DefaultDurationSec { get; init; } = 0.5;
        public double PreviewProgress { get; init; } = 0.5;
        public IReadOnlyList<ParamDef> ParamSchema { get; init; } = Array.Empty<ParamDef>();

        public Dictionary<string, ParamValue> DefaultParams()
        {
            var map = new Dictionary<string, ParamValue>();
            foreach (var p in ParamSchema)
                map[p.Key] = p.DefaultValue();
            return map;
        }

        public TransitionRef CreateRef(double? durationSec = null)
        {
            return new TransitionRef
            {
                TypeId = TypeId,
                DurationSec = durationSec ?? DefaultDurationSec,
                Params = DefaultParams(),
            };
        }
    }
}
