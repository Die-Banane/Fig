using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>One node type. Stateless: everything it needs arrives in the <see cref="PrimitiveContext"/>.</summary>
    public interface IGraphPrimitive
    {
        /// <summary>
        /// Produces this node's outputs keyed by output port id. May return an input frame unchanged
        /// (a pass-through); never mutates an input. New frames should come from <see cref="FramePool"/>.
        /// </summary>
        IReadOnlyDictionary<string, DecodedFrame> Evaluate(PrimitiveContext context);
    }

    public sealed record PortDef(string Id, string Label, GraphPortType Type, bool Required = true);

    public sealed class PrimitiveDescriptor
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Category { get; init; } = "Other";
        public string Description { get; init; } = "";
        public IReadOnlyList<PortDef> Inputs { get; init; } = Array.Empty<PortDef>();
        public IReadOnlyList<PortDef> Outputs { get; init; } = Array.Empty<PortDef>();
        public IReadOnlyList<ParamDef> ParamSchema { get; init; } = Array.Empty<ParamDef>();

        public PortDef? Input(string id) => Inputs.FirstOrDefault(p => p.Id == id);
        public PortDef? Output(string id) => Outputs.FirstOrDefault(p => p.Id == id);
        public ParamDef? Param(string key) => ParamSchema.FirstOrDefault(p => p.Key == key);
    }

    /// <summary>What a primitive sees for one evaluation.</summary>
    public sealed class PrimitiveContext
    {
        private readonly IReadOnlyDictionary<string, DecodedFrame> _inputs;
        private readonly IReadOnlyDictionary<string, ParamValue> _params;
        private readonly PrimitiveDescriptor _descriptor;

        public PrimitiveContext(PrimitiveDescriptor descriptor, GraphNode node, DecodedFrame source,
            IReadOnlyDictionary<string, DecodedFrame> inputs, IReadOnlyDictionary<string, ParamValue> parameters, double localT)
        {
            _descriptor = descriptor;
            Node = node;
            Source = source;
            _inputs = inputs;
            _params = parameters;
            LocalT = localT;
        }

        public GraphNode Node { get; }

        /// <summary>The frame the effect was applied to (what an Input node emits).</summary>
        public DecodedFrame Source { get; }

        /// <summary>Seconds from the clip start (or the track timeline for track effects).</summary>
        public double LocalT { get; }

        /// <summary>The effective parameter values: node values with promoted controls applied.</summary>
        public IReadOnlyDictionary<string, ParamValue> Params => _params;

        public DecodedFrame Input(string port) => _inputs[port];
        public DecodedFrame? OptionalInput(string port) => _inputs.TryGetValue(port, out var f) ? f : null;

        public double Number(string key)
        {
            var def = _descriptor.Param(key);
            var fallback = def?.Default ?? 0;
            var value = _params.TryGetValue(key, out var v) && v.Kind is ParamKind.Double or ParamKind.Int ? v.AsNumber : fallback;
            if (!double.IsFinite(value))
                value = fallback;
            return def is not null && def.Max > def.Min ? Math.Clamp(value, def.Min, def.Max) : value;
        }

        public int Int(string key) => (int)Math.Round(Number(key));

        public bool Bool(string key)
            => _params.TryGetValue(key, out var v) && v.Kind == ParamKind.Bool
                ? v.AsBool
                : (_descriptor.Param(key)?.Default ?? 0) != 0;

        public int Choice(string key)
        {
            var def = _descriptor.Param(key);
            var value = _params.TryGetValue(key, out var v) && v.Kind == ParamKind.List ? v.AsChoice : (int)Math.Round(def?.Default ?? 0);
            var count = def?.Choices.Count ?? 0;
            return count > 0 ? Math.Clamp(value, 0, count - 1) : value;
        }

        public uint Color(string key)
            => _params.TryGetValue(key, out var v) && v.Kind == ParamKind.Color
                ? v.AsColor
                : (uint)(_descriptor.Param(key)?.Default ?? 0);
    }
}
