using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline;

/// <summary>What flows along a wire. Wires only connect ports of the same type.</summary>
public enum GraphPortType
{
    Image,
    Mask,
}

/// <summary>
/// A user-built effect: primitive nodes wired into an acyclic graph that ends in one Output node.
/// Saved graphs become catalog effects with the type id <c>graph:&lt;id&gt;</c>, so clips,
/// keyframes, presets and the inspector treat them like any built-in.
/// </summary>
public sealed class EffectGraph
{
    public const string TypeIdPrefix = "graph:";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled effect";
    public int SchemaVersion { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();

    /// <summary>The controls this effect shows on a clip, each bound to one or more node parameters.</summary>
    public List<PromotedParam> Promoted { get; set; } = new();

    /// <summary>The effect type id clips store while this graph is in the library.</summary>
    [JsonIgnore]
    public string TypeId => TypeIdPrefix + Id;

    public static bool IsGraphTypeId(string? typeId)
        => typeId is not null && typeId.StartsWith(TypeIdPrefix, StringComparison.Ordinal);

    public GraphNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public EffectGraph Clone() => new()
    {
        Id = Id,
        Name = Name,
        SchemaVersion = SchemaVersion,
        UpdatedAt = UpdatedAt,
        Nodes = Nodes.Select(n => n.Clone()).ToList(),
        Edges = Edges.Select(e => e.Clone()).ToList(),
        Promoted = Promoted.Select(p => p.Clone()).ToList(),
    };
}

public sealed class GraphNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The primitive this node runs (for example <c>blur</c>, <c>mix</c>, <c>effect</c>).</summary>
    public string PrimitiveId { get; set; } = "";

    /// <summary>For the <c>effect</c> primitive only: the stock effect it wraps. <see cref="Params"/> are that effect's.</summary>
    public string? EffectTypeId { get; set; }

    public Dictionary<string, ParamValue> Params { get; set; } = new();

    /// <summary>Passes the input straight through. Only meaningful for nodes with one input and one output of the same type.</summary>
    public bool Bypass { get; set; }

    /// <summary>Editor position in canvas units. View state only; rendering never reads it.</summary>
    public double X { get; set; }
    public double Y { get; set; }

    public GraphNode Clone() => new()
    {
        Id = Id,
        PrimitiveId = PrimitiveId,
        EffectTypeId = EffectTypeId,
        Params = new Dictionary<string, ParamValue>(Params),
        Bypass = Bypass,
        X = X,
        Y = Y,
    };
}

public sealed class GraphEdge
{
    public string FromNodeId { get; set; } = "";
    public string FromPort { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public string ToPort { get; set; } = "";

    public GraphEdge Clone() => new()
    {
        FromNodeId = FromNodeId,
        FromPort = FromPort,
        ToNodeId = ToNodeId,
        ToPort = ToPort,
    };
}

/// <summary>One control the saved effect exposes. A single control may drive several node parameters.</summary>
public sealed class PromotedParam
{
    /// <summary>The key on the effect (becomes the <see cref="ParamDef.Key"/> and the key in <see cref="EffectInstance.Params"/>).</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public List<PromotedTarget> Targets { get; set; } = new();

    /// <summary>Optional overrides of the first target's range and default.</summary>
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Default { get; set; }

    public PromotedParam Clone() => new()
    {
        Key = Key,
        Label = Label,
        Targets = Targets.Select(t => t.Clone()).ToList(),
        Min = Min,
        Max = Max,
        Default = Default,
    };
}

public sealed class PromotedTarget
{
    public string NodeId { get; set; } = "";
    public string Param { get; set; } = "";

    /// <summary>Multiplier applied to numeric values, so one control can drive a node in the opposite direction (-1).</summary>
    public double Scale { get; set; } = 1;

    public PromotedTarget Clone() => new() { NodeId = NodeId, Param = Param, Scale = Scale };
}
