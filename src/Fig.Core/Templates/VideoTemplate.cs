using System.Text.Json.Serialization;
using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Templates;

/// <summary>
/// A reusable edit blueprint. Media clip SourceId values in <see cref="Timeline"/>
/// refer to <see cref="TemplateSlot.Id"/> until the template is instantiated.
/// </summary>
public sealed class VideoTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled template";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "My templates";
    public List<string> Tags { get; set; } = new();
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public TimelineModel Timeline { get; set; } = new() { Rate = FrameRate.Common(30) };
    public List<TemplateSlot> Slots { get; set; } = new();

    [JsonIgnore]
    public double DurationSec => Timeline.Tracks
        .SelectMany(track => track.Clips)
        .Select(clip => clip.StartSec + clip.DurSec)
        .DefaultIfEmpty()
        .Max();

    [JsonIgnore]
    public string DurationLabel => TimeSpan.FromSeconds(DurationSec).ToString(DurationSec >= 60 ? @"m\:ss" : @"s\:ss");

    [JsonIgnore]
    public string SlotLabel => Slots.Count == 1 ? "1 media slot" : $"{Slots.Count} media slots";

    [JsonIgnore]
    public string CanvasLabel
    {
        get
        {
            var divisor = GreatestCommonDivisor(Math.Max(CanvasWidth, 1), Math.Max(CanvasHeight, 1));
            return $"{CanvasWidth / divisor}:{CanvasHeight / divisor}";
        }
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}

public sealed class TemplateSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Media";
    public MediaKind Kind { get; set; } = MediaKind.Video;
    /// <summary>
    /// The amount of timeline time designed for this placeholder. This is presentation
    /// metadata, not a minimum source-media requirement: longer media is trimmed and
    /// shorter media remains valid while the template keeps its authored timing.
    /// </summary>
    public double TargetDurationSec { get; set; }
    public bool Required { get; set; } = true;
}
