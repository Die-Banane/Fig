using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Runs one <see cref="EffectGraph"/> as an effect. The graph is compiled once at construction;
    /// an invalid graph, or any failure while rendering, passes the picture through unchanged.
    /// </summary>
    public sealed class GraphEffectProcessor : IEffectProcessor
    {
        private readonly EffectGraph _graph;
        private readonly GraphPlan? _plan;

        public GraphEffectProcessor(EffectGraph graph)
        {
            _graph = graph.Clone();
            Compile = GraphCompiler.Compile(_graph);
            _plan = Compile.Plan;
        }

        public CompileResult Compile { get; }

        public string TypeId => _graph.TypeId;

        public DecodedFrame Apply(DecodedFrame frame, IReadOnlyDictionary<string, ParamValue> parameters, double localT)
        {
            if (_plan is null || frame.Width <= 0 || frame.Height <= 0)
                return frame;

            var overrides = BuildOverrides(parameters);
            var references = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
            var values = new Dictionary<(string, string), DecodedFrame>();
            var rented = new List<byte[]>(); // everything we allocated, for cleanup on failure
            references[frame.Pixels] = 1; // the source is the caller's; it is never returned from here

            void Hold(DecodedFrame f, int count)
                => references[f.Pixels] = references.GetValueOrDefault(f.Pixels) + count;

            void Release(DecodedFrame f)
            {
                if (!references.TryGetValue(f.Pixels, out var n))
                    return;
                references[f.Pixels] = --n;
                if (n <= 0 && !ReferenceEquals(f.Pixels, frame.Pixels))
                    FramePool.Return(f.Pixels);
            }

            try
            {
                DecodedFrame? result = null;
                foreach (var step in _plan.Steps)
                {
                    var inputs = new Dictionary<string, DecodedFrame>();
                    foreach (var (port, src) in step.Inputs)
                        inputs[port] = values[src];

                    if (step.IsOutput)
                    {
                        // the output's input is the result: ownership moves to the caller, no release
                        result = inputs["image"];
                        break;
                    }

                    IReadOnlyDictionary<string, DecodedFrame> produced;
                    if (step.Bypassed)
                    {
                        produced = new Dictionary<string, DecodedFrame> { [step.Descriptor.Outputs[0].Id] = inputs[step.Descriptor.Inputs[0].Id] };
                    }
                    else
                    {
                        var context = new PrimitiveContext(step.Descriptor, step.Node, frame, inputs,
                            ParamsFor(step.Node, overrides), localT);
                        produced = step.Primitive.Evaluate(context);
                    }

                    foreach (var output in step.Descriptor.Outputs)
                    {
                        if (!produced.TryGetValue(output.Id, out var f))
                            continue;
                        var readers = _plan.Consumers.GetValueOrDefault((step.Node.Id, output.Id));
                        if (!ReferenceEquals(f.Pixels, frame.Pixels) && !references.ContainsKey(f.Pixels))
                            rented.Add(f.Pixels);
                        values[(step.Node.Id, output.Id)] = f;
                        Hold(f, readers);
                        if (readers == 0 && references.GetValueOrDefault(f.Pixels) <= 0 && !ReferenceEquals(f.Pixels, frame.Pixels))
                            FramePool.Return(f.Pixels);
                    }

                    foreach (var src in step.Inputs.Values)
                        Release(values[src]);
                }

                if (result is null)
                    return frame;
                if (ReferenceEquals(result.Pixels, frame.Pixels))
                    return frame;
                // Hand the caller a frame it owns: other held buffers are already back in the pool.
                return result;
            }
            catch (Exception)
            {
                foreach (var pixels in rented)
                    FramePool.Return(pixels);
                return frame;
            }
        }

        /// <summary>Promoted control values (with their scale) keyed by target node id, then parameter.</summary>
        private Dictionary<string, Dictionary<string, ParamValue>> BuildOverrides(IReadOnlyDictionary<string, ParamValue> parameters)
        {
            var map = new Dictionary<string, Dictionary<string, ParamValue>>();
            foreach (var promoted in _graph.Promoted)
            {
                if (!parameters.TryGetValue(promoted.Key, out var value))
                    continue;
                foreach (var target in promoted.Targets)
                {
                    if (!map.TryGetValue(target.NodeId, out var perNode))
                        map[target.NodeId] = perNode = new Dictionary<string, ParamValue>();
                    perNode[target.Param] = Scaled(value, target.Scale);
                }
            }
            return map;
        }

        private static ParamValue Scaled(ParamValue value, double scale)
        {
            if (scale == 1 || value.Kind is not (ParamKind.Double or ParamKind.Int))
                return value;
            var scaled = value.AsNumber * scale;
            return value.Kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(scaled)) : ParamValue.OfDouble(scaled);
        }

        private static IReadOnlyDictionary<string, ParamValue> ParamsFor(
            GraphNode node, Dictionary<string, Dictionary<string, ParamValue>> overrides)
        {
            if (!overrides.TryGetValue(node.Id, out var extra))
                return node.Params;
            var merged = new Dictionary<string, ParamValue>(node.Params);
            foreach (var (key, value) in extra)
                merged[key] = value;
            return merged;
        }
    }
}
