using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// The user's graph effects for the open project. Saving validates the graph and publishes it to
    /// <see cref="EffectCatalog"/> under the category "My effects", so it can be dropped on a clip,
    /// keyframed and saved in presets like a built-in. The graphs live in a list owned by the project.
    /// </summary>
    public sealed class GraphEffectLibrary
    {
        public const string Category = "My effects";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private List<EffectGraph> _graphs = new();

        public event Action? Changed;

        public IReadOnlyList<EffectGraph> Graphs => _graphs;

        /// <summary>Uses <paramref name="graphs"/> (usually the project's own list) as the library and publishes it.</summary>
        public void Attach(List<EffectGraph> graphs)
        {
            _graphs = graphs;
            Publish();
        }

        /// <summary>Stops publishing this library's effects (the editor closed or the project changed).</summary>
        public void Detach()
        {
            EffectCatalog.ClearUserEffects(this);
            _graphs = new List<EffectGraph>();
        }

        public EffectGraph? Find(string id) => _graphs.FirstOrDefault(g => g.Id == id);

        /// <summary>
        /// Validates and stores a copy of the graph, replacing any saved graph with the same id.
        /// An invalid graph is not stored; the result says why.
        /// </summary>
        public CompileResult Save(EffectGraph graph)
        {
            var result = GraphCompiler.Compile(graph);
            if (!result.IsValid)
                return result;
            var copy = graph.Clone();
            if (string.IsNullOrWhiteSpace(copy.Name))
                copy.Name = "Untitled effect";
            copy.UpdatedAt = DateTime.UtcNow;
            var index = _graphs.FindIndex(g => g.Id == copy.Id);
            if (index >= 0)
                _graphs[index] = copy;
            else
                _graphs.Add(copy);
            Publish();
            return result;
        }

        public bool Delete(string id)
        {
            var removed = _graphs.RemoveAll(g => g.Id == id) > 0;
            if (removed)
                Publish();
            return removed;
        }

        /// <summary>Copies a saved graph under a new id and name.</summary>
        public EffectGraph? Duplicate(string id)
        {
            var source = Find(id);
            if (source is null)
                return null;
            var copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = source.Name + " copy";
            _graphs.Add(copy);
            Publish();
            return copy;
        }

        public void Export(string id, string path)
        {
            var graph = Find(id) ?? throw new InvalidOperationException("No such effect.");
            File.WriteAllText(path, JsonSerializer.Serialize(graph, Options));
        }

        /// <summary>Reads an exported graph. A graph whose id is already taken is imported as a new one.</summary>
        public EffectGraph Import(string path)
        {
            var graph = JsonSerializer.Deserialize<EffectGraph>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("The file is not an effect.");
            if (!GraphCompiler.Compile(graph).IsValid)
                throw new InvalidDataException("The effect in this file is not valid.");
            if (_graphs.Any(g => g.Id == graph.Id))
                graph.Id = Guid.NewGuid().ToString("N");
            _graphs.Add(graph);
            Publish();
            return graph;
        }

        /// <summary>The catalog entry a graph publishes: its promoted controls become the effect's parameters.</summary>
        public static EffectCatalogEntry BuildEntry(EffectGraph graph)
        {
            var schema = new List<ParamDef>();
            foreach (var promoted in graph.Promoted)
            {
                var target = promoted.Targets.FirstOrDefault();
                var node = target is null ? null : graph.FindNode(target.NodeId);
                var def = node is null ? null : PrimitiveCatalog.ParamSchemaFor(node).FirstOrDefault(p => p.Key == target!.Param);
                if (def is null)
                    continue;
                var current = node!.Params.TryGetValue(def.Key, out var v) ? ToDouble(v) : def.Default;
                schema.Add(new ParamDef
                {
                    Key = promoted.Key,
                    Label = string.IsNullOrWhiteSpace(promoted.Label) ? def.Label : promoted.Label,
                    Kind = def.Kind,
                    Min = promoted.Min ?? def.Min,
                    Max = promoted.Max ?? def.Max,
                    Default = promoted.Default ?? current,
                    Choices = def.Choices,
                });
            }
            return new EffectCatalogEntry
            {
                TypeId = graph.TypeId,
                Category = Category,
                Kind = EffectKind.Video,
                DisplayName = graph.Name,
                Icon = "wand-sparkles",
                Description = "A custom effect.",
                IsCustom = true,
                ParamSchema = schema,
            };
        }

        private static double ToDouble(ParamValue v) => v.Kind switch
        {
            ParamKind.Bool => v.AsBool ? 1 : 0,
            ParamKind.Color => v.AsColor,
            ParamKind.List => v.AsChoice,
            _ => v.AsNumber,
        };

        private void Publish()
        {
            EffectCatalog.SetUserEffects(this, _graphs.Select(g => (BuildEntry(g), (IEffectProcessor)new GraphEffectProcessor(g))));
            Changed?.Invoke();
        }
    }
}
