using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Media
{
    /// <summary>User presets saved as small JSON files, one per preset.</summary>
    public sealed class EffectPresetStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string RootDirectory { get; }

        public EffectPresetStore(string rootDirectory) => RootDirectory = rootDirectory;

        public IReadOnlyList<EffectPreset> List()
        {
            if (!Directory.Exists(RootDirectory))
                return Array.Empty<EffectPreset>();
            return Directory.GetFiles(RootDirectory, "*.figpreset.json")
                .Select(Load)
                .Where(p => p is not null)
                .Cast<EffectPreset>()
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void Save(EffectPreset preset)
        {
            Directory.CreateDirectory(RootDirectory);
            preset.BuiltIn = false;
            File.WriteAllText(PathFor(preset.Id), JsonSerializer.Serialize(preset, Options));
        }

        public bool Delete(string id)
        {
            var path = PathFor(id);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        private string PathFor(string id)
            => Path.Combine(RootDirectory, new string(id.Where(char.IsLetterOrDigit).ToArray()) + ".figpreset.json");

        private static EffectPreset? Load(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<EffectPreset>(File.ReadAllText(path), Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
