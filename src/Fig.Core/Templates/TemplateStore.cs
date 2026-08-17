using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Templates;

public sealed class TemplateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string RootDirectory { get; }

    public TemplateStore(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public IReadOnlyList<VideoTemplate> List()
    {
        if (!Directory.Exists(RootDirectory))
            return Array.Empty<VideoTemplate>();
        return Directory.GetFiles(RootDirectory, "*.figtemplate.json")
            .Select(LoadFile)
            .Where(template => template is not null)
            .Cast<VideoTemplate>()
            .OrderByDescending(template => template.CreatedAt)
            .ToList();
    }

    public VideoTemplate? Load(string id)
    {
        var path = Path.Combine(RootDirectory, $"{SafeId(id)}.figtemplate.json");
        return File.Exists(path) ? LoadFile(path) : null;
    }

    public void Save(VideoTemplate template)
    {
        Directory.CreateDirectory(RootDirectory);
        var path = Path.Combine(RootDirectory, $"{SafeId(template.Id)}.figtemplate.json");
        File.WriteAllText(path, JsonSerializer.Serialize(template, Options));
    }

    private static VideoTemplate? LoadFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<VideoTemplate>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SafeId(string id) => new(id.Where(char.IsLetterOrDigit).ToArray());
}
