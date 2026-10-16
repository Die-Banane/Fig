using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Fig.App.Services
{
    public sealed class PreferenceLoader(string path)
    {

        private readonly string _path = path;
        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true
        };

        public Dictionary<string, JsonElement> Load()
        {
            if (!File.Exists(_path))
                // if the config file path does not exist, create a new one
                return [];

            try
            {
                var text = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? new();

            }
            catch (JsonException)
            {
                // resolve corrupted
                File.Move(_path, _path + ".bad", overwrite: true);
                return []; // generate a new pref from defaults
            }
        }

        public void Save(IReadOnlyDictionary<string, JsonElement> values) {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(values, Indented));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
