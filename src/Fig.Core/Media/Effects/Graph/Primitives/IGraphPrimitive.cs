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
        private readonly DecodedFrame? _source;

        public PrimitiveContext(PrimitiveDescriptor descriptor, GraphNode node, DecodedFrame source,
            IReadOnlyDictionary<string, DecodedFrame> inputs, IReadOnlyDictionary<string, ParamValue> parameters, double localT)
        {
            _descriptor = descriptor;
            Node = node;
            _source = source;
            _inputs = inputs;
            _params = parameters;
            LocalT = localT;
        }

        /// <summary>
        /// A context that only reads parameters (no pictures). The GPU renderer uses it so a node's values
        /// are clamped and defaulted exactly as the CPU primitive sees them.
        /// </summary>
        public static PrimitiveContext ForParameters(PrimitiveDescriptor descriptor, GraphNode node,
            IReadOnlyDictionary<string, ParamValue> parameters, double localT)
            => new(descriptor, node, null!, new Dictionary<string, DecodedFrame>(), parameters, localT);

        public GraphNode Node { get; }

        /// <summary>The frame the effect was applied to (what an Input node emits).</summary>
        public DecodedFrame Source => _source ?? throw new InvalidOperationException("This context carries parameters only.");

        /// <summary>Seconds from the clip start (or the track timeline for track effects).</summary>
        public double LocalT { get; }

        /// <summary>The effective parameter values: node values with promoted controls applied.</summary>
        public IReadOnlyDictionary<string, ParamValue> Params => _params;

        public DecodedFrame Input(string port) => _inputs[port];
        public DecodedFrame? OptionalInput(string port) => _inputs.TryGetValue(port, out var f) ? f : null;

        /// <summary>The parameters read through the primitive's schema (defaulted and clamped).</summary>
        private ParamSet Read => new(_descriptor.ParamSchema, _params);

        public double Number(string key) => Read.Number(key);
        public int Int(string key) => Read.Int(key);
        public bool Bool(string key) => Read.Bool(key);
        public int Choice(string key) => Read.Choice(key);
        public uint Color(string key) => Read.Color(key);
    }
}
