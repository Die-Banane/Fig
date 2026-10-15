using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// The graph primitives, discovered from every <see cref="IGraphPrimitive"/> in this assembly that
    /// carries a <see cref="PrimitiveAttribute"/>. Adding a node type is one class.
    /// </summary>
    public static class PrimitiveCatalog
    {
        public const string Input = "input";
        public const string Output = "output";
        public const string StockEffect = "effect";

        private sealed record Entry(PrimitiveDescriptor Descriptor, IGraphPrimitive Primitive);

        private static readonly Lazy<IReadOnlyDictionary<string, Entry>> Entries = new(Discover);

        private static readonly Lazy<IReadOnlyList<PrimitiveDescriptor>> Sorted = new(() =>
            Entries.Value.Values.Select(e => e.Descriptor).OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList());

        public static IReadOnlyList<PrimitiveDescriptor> All => Sorted.Value;

        public static PrimitiveDescriptor? Find(string id)
            => Entries.Value.TryGetValue(id, out var e) ? e.Descriptor : null;

        public static IGraphPrimitive? Resolve(string id)
            => Entries.Value.TryGetValue(id, out var e) ? e.Primitive : null;

        /// <summary>
        /// The parameter schema a node actually has: the primitive's own, except a stock-effect node
        /// which shows the wrapped effect's parameters.
        /// </summary>
        public static IReadOnlyList<ParamDef> ParamSchemaFor(GraphNode node)
        {
            if (node.PrimitiveId == StockEffect)
                return node.EffectTypeId is { } id && EffectCatalog.Find(id) is { } fx ? fx.ParamSchema : Array.Empty<ParamDef>();
            return Find(node.PrimitiveId)?.ParamSchema ?? Array.Empty<ParamDef>();
        }

        private static IReadOnlyDictionary<string, Entry> Discover()
        {
            var map = new Dictionary<string, Entry>();
            foreach (var type in typeof(IGraphPrimitive).Assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IGraphPrimitive).IsAssignableFrom(type))
                    continue;
                var attr = type.GetCustomAttribute<PrimitiveAttribute>();
                if (attr is null)
                    continue;
                var descriptor = new PrimitiveDescriptor
                {
                    Id = attr.Id,
                    DisplayName = attr.DisplayName,
                    Category = attr.Category,
                    Description = attr.Description,
                    Inputs = type.GetCustomAttributes<PrimitiveInputAttribute>()
                        .Select(a => new PortDef(a.Id, a.Label, a.Type, a.Required)).ToList(),
                    Outputs = type.GetCustomAttributes<PrimitiveOutputAttribute>()
                        .Select(a => new PortDef(a.Id, a.Label, a.Type)).ToList(),
                    ParamSchema = type.GetCustomAttributes<PrimitiveParamAttribute>()
                        .Select(p => new ParamDef
                        {
                            Key = p.Key,
                            Label = p.Label,
                            Kind = p.Kind,
                            Default = p.Default,
                            Min = p.Min,
                            Max = p.Max,
                            Choices = p.Choices,
                        }).ToList(),
                };
                map[attr.Id] = new Entry(descriptor, (IGraphPrimitive)Activator.CreateInstance(type)!);
            }
            return map;
        }
    }
}
