using System.IO;
using System.Text.Json;

namespace Fig.App.Services
{
    /// <summary>Reads and writes preferences.json. Never throws on a bad file: it falls back to defaults.</summary>
    public sealed class PreferenceLoader(string path)
    {
        private readonly string _path = path;
        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true
        };

        public Preferences Load()
        {
            if (!File.Exists(_path))
                // first run: nothing saved yet, so every value is its default
                return new();

            try
            {
                var text = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Preferences>(text) ?? new();
            }
            catch (JsonException)
            {
                // keep the broken file so it can be inspected, then start from defaults
                File.Move(_path, _path + ".bad", overwrite: true);
                return new();
            }
        }

        public void Save(Preferences preferences)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(preferences, Indented));
            // a rename replaces the file in one step, so a crash mid-write never leaves half a file
            File.Move(temp, _path, overwrite: true);
        }
    }
}
