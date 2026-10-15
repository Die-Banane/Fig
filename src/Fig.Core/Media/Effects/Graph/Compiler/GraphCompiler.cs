using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    public enum GraphErrorCode
    {
        NoOutput,
        MultipleOutputs,
        UnknownPrimitive,
        UnknownEffect,
        DuplicateNode,
        BadEdge,
        TypeMismatch,
        InputAlreadyConnected,
        MissingInput,
        Cycle,
        BadPromotedParam,
    }

    /// <summary>A problem the editor can point at: a node when <see cref="NodeId"/> is set, the whole graph otherwise.</summary>
    public sealed record GraphError(GraphErrorCode Code, string Message, string? NodeId = null);

    /// <summary>One node to run, in dependency order.</summary>
    public sealed class PlanStep
    {
        public GraphNode Node { get; init; } = null!;
        public PrimitiveDescriptor Descriptor { get; init; } = null!;
        public IGraphPrimitive Primitive { get; init; } = null!;

        /// <summary>Input port id → the node output feeding it.</summary>
        public IReadOnlyDictionary<string, (string NodeId, string Port)> Inputs { get; init; }
            = new Dictionary<string, (string, string)>();

        /// <summary>True when the node is bypassed and its input passes straight through.</summary>
        public bool Bypassed { get; init; }

        public bool IsOutput => Node.PrimitiveId == PrimitiveCatalog.Output;
    }

    public sealed class GraphPlan
    {
        public IReadOnlyList<PlanStep> Steps { get; init; } = Array.Empty<PlanStep>();

        /// <summary>How many nodes read each output port: (node id, port id) → readers.</summary>
        public IReadOnlyDictionary<(string NodeId, string Port), int> Consumers { get; init; }
            = new Dictionary<(string, string), int>();
    }

    public sealed class CompileResult
    {
        public GraphPlan? Plan { get; init; }
        public IReadOnlyList<GraphError> Errors { get; init; } = Array.Empty<GraphError>();

        /// <summary>Nodes that do not feed the output. They are ignored when rendering.</summary>
        public IReadOnlyList<string> UnusedNodeIds { get; init; } = Array.Empty<string>();

        public bool IsValid => Plan is not null && Errors.Count == 0;
    }

    /// <summary>Validates a graph and turns it into an ordered plan. Pure: no rendering, no side effects.</summary>
    public static class GraphCompiler
    {
        public static CompileResult Compile(EffectGraph graph)
        {
            var errors = new List<GraphError>();
            var byId = new Dictionary<string, GraphNode>();
            var descriptors = new Dictionary<string, PrimitiveDescriptor>();

            foreach (var node in graph.Nodes)
            {
                if (!byId.TryAdd(node.Id, node))
                {
                    errors.Add(new GraphError(GraphErrorCode.DuplicateNode, "Two nodes share the same id.", node.Id));
                    continue;
                }
                var descriptor = PrimitiveCatalog.Find(node.PrimitiveId);
                if (descriptor is null)
                {
                    errors.Add(new GraphError(GraphErrorCode.UnknownPrimitive, $"Unknown node type \"{node.PrimitiveId}\".", node.Id));
                    continue;
                }
                descriptors[node.Id] = descriptor;
                if (node.PrimitiveId == PrimitiveCatalog.StockEffect)
                {
                    var entry = node.EffectTypeId is null || EffectGraph.IsGraphTypeId(node.EffectTypeId)
                        ? null : EffectCatalog.Find(node.EffectTypeId);
                    if (entry is null || entry.Kind == EffectKind.Audio)
                        errors.Add(new GraphError(GraphErrorCode.UnknownEffect, "This effect node has no usable effect.", node.Id));
                }
            }

            var outputs = graph.Nodes.Where(n => n.PrimitiveId == PrimitiveCatalog.Output).ToList();
            if (outputs.Count == 0)
                errors.Add(new GraphError(GraphErrorCode.NoOutput, "The graph has no Output node."));
            else if (outputs.Count > 1)
                foreach (var extra in outputs.Skip(1))
                    errors.Add(new GraphError(GraphErrorCode.MultipleOutputs, "Only one Output node is allowed.", extra.Id));

            // edges: endpoints, ports, types, one wire per input
            var incoming = new Dictionary<(string, string), (string NodeId, string Port)>();
            foreach (var edge in graph.Edges)
            {
                if (!descriptors.TryGetValue(edge.FromNodeId, out var from) || !descriptors.TryGetValue(edge.ToNodeId, out var to))
                {
                    errors.Add(new GraphError(GraphErrorCode.BadEdge, "A wire points at a node that does not exist.", edge.ToNodeId));
                    continue;
                }
                var outPort = from.Output(edge.FromPort);
                var inPort = to.Input(edge.ToPort);
                if (outPort is null || inPort is null)
                {
                    errors.Add(new GraphError(GraphErrorCode.BadEdge, "A wire uses a port that does not exist.", edge.ToNodeId));
                    continue;
                }
                if (outPort.Type != inPort.Type)
                {
                    errors.Add(new GraphError(GraphErrorCode.TypeMismatch,
                        $"{outPort.Type} output cannot connect to a {inPort.Type} input.", edge.ToNodeId));
                    continue;
                }
                if (!incoming.TryAdd((edge.ToNodeId, edge.ToPort), (edge.FromNodeId, edge.FromPort)))
                    errors.Add(new GraphError(GraphErrorCode.InputAlreadyConnected,
                        $"\"{inPort.Label}\" already has a wire.", edge.ToNodeId));
            }

            // reachability from the output, post-order = dependency order; a back edge is a cycle
            var order = new List<GraphNode>();
            if (outputs.Count >= 1)
            {
                var state = new Dictionary<string, int>(); // 1 visiting, 2 done
                var reported = false;
                void Visit(GraphNode node)
                {
                    if (state.TryGetValue(node.Id, out var s))
                    {
                        if (s == 1 && !reported)
                        {
                            reported = true;
                            errors.Add(new GraphError(GraphErrorCode.Cycle, "The wires form a loop.", node.Id));
                        }
                        return;
                    }
                    state[node.Id] = 1;
                    if (descriptors.TryGetValue(node.Id, out var d))
                        foreach (var port in d.Inputs)
                            if (incoming.TryGetValue((node.Id, port.Id), out var src) && byId.TryGetValue(src.NodeId, out var upstream))
                                Visit(upstream);
                    state[node.Id] = 2;
                    order.Add(node);
                }
                Visit(outputs[0]);
            }

            // required inputs on nodes that matter
            foreach (var node in order)
            {
                if (!descriptors.TryGetValue(node.Id, out var d))
                    continue;
                foreach (var port in d.Inputs.Where(p => p.Required))
                    if (!incoming.ContainsKey((node.Id, port.Id)))
                        errors.Add(new GraphError(GraphErrorCode.MissingInput, $"\"{port.Label}\" is not connected.", node.Id));
            }

            ValidatePromoted(graph, byId, errors);

            var used = new HashSet<string>(order.Select(n => n.Id));
            var unused = graph.Nodes.Where(n => !used.Contains(n.Id)).Select(n => n.Id).ToList();
            if (errors.Count > 0 || order.Count == 0)
                return new CompileResult { Errors = errors, UnusedNodeIds = unused };

            var consumers = new Dictionary<(string, string), int>();
            var steps = new List<PlanStep>();
            foreach (var node in order)
            {
                var d = descriptors[node.Id];
                var inputs = new Dictionary<string, (string, string)>();
                foreach (var port in d.Inputs)
                    if (incoming.TryGetValue((node.Id, port.Id), out var src))
                    {
                        inputs[port.Id] = src;
                        consumers[src] = consumers.GetValueOrDefault(src) + 1;
                    }
                var bypass = node.Bypass && d.Inputs.Count == 1 && d.Outputs.Count == 1 && d.Inputs[0].Type == d.Outputs[0].Type;
                steps.Add(new PlanStep
                {
                    Node = node,
                    Descriptor = d,
                    Primitive = PrimitiveCatalog.Resolve(node.PrimitiveId)!,
                    Inputs = inputs,
                    Bypassed = bypass,
                });
            }
            return new CompileResult { Plan = new GraphPlan { Steps = steps, Consumers = consumers }, UnusedNodeIds = unused };
        }

        private static void ValidatePromoted(EffectGraph graph, Dictionary<string, GraphNode> byId, List<GraphError> errors)
        {
            var keys = new HashSet<string>();
            foreach (var promoted in graph.Promoted)
            {
                if (string.IsNullOrWhiteSpace(promoted.Key) || !keys.Add(promoted.Key))
                {
                    errors.Add(new GraphError(GraphErrorCode.BadPromotedParam,
                        $"Control \"{promoted.Label}\" needs a unique key."));
                    continue;
                }
                if (promoted.Targets.Count == 0)
                {
                    errors.Add(new GraphError(GraphErrorCode.BadPromotedParam, $"Control \"{promoted.Label}\" is not linked to anything."));
                    continue;
                }
                foreach (var target in promoted.Targets)
                {
                    if (!byId.TryGetValue(target.NodeId, out var node)
                        || !PrimitiveCatalog.ParamSchemaFor(node).Any(p => p.Key == target.Param))
                        errors.Add(new GraphError(GraphErrorCode.BadPromotedParam,
                            $"Control \"{promoted.Label}\" points at a parameter that no longer exists.", target.NodeId));
                }
            }
        }
    }
}
