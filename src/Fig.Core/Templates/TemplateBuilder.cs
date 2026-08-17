using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Templates;

public static class TemplateBuilder
{
    public static VideoTemplate FromTimeline(
        string name,
        string category,
        TimelineModel timeline,
        IEnumerable<MediaAsset> media,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var mediaById = media.ToDictionary(asset => asset.Id);
        var blueprint = TemplateTimeline.Clone(timeline, freshIds: false);
        var slots = new List<TemplateSlot>();
        var slotBySource = new Dictionary<string, TemplateSlot>();

        foreach (var clip in blueprint.Tracks.SelectMany(track => track.Clips))
        {
            var sourceId = clip switch
            {
                VideoClip video => video.SourceId,
                AudioClip audio => audio.SourceId,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(sourceId))
                continue;

            if (!slotBySource.TryGetValue(sourceId, out var slot))
            {
                mediaById.TryGetValue(sourceId, out var asset);
                slot = new TemplateSlot
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = asset is null ? $"Media {slots.Count + 1}" : Path.GetFileNameWithoutExtension(asset.FileName),
                    Kind = asset?.Kind ?? (clip is AudioClip ? MediaKind.Audio : MediaKind.Video),
                    TargetDurationSec = clip.SourceOut > clip.SourceIn
                        ? clip.SourceOut - clip.SourceIn
                        : clip.DurSec * Math.Max(clip.Speed, 0.01),
                };
                slotBySource[sourceId] = slot;
                slots.Add(slot);
            }
            else
            {
                var required = clip.SourceOut > clip.SourceIn
                    ? clip.SourceOut - clip.SourceIn
                    : clip.DurSec * Math.Max(clip.Speed, 0.01);
                slot.TargetDurationSec = Math.Max(slot.TargetDurationSec, required);
            }

            if (clip is VideoClip videoClip)
                videoClip.SourceId = slot.Id;
            else if (clip is AudioClip audioClip)
                audioClip.SourceId = slot.Id;
        }

        return new VideoTemplate
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled template" : name.Trim(),
            Description = description?.Trim() ?? "A reusable edit created in Fig.",
            Category = string.IsNullOrWhiteSpace(category) ? "My templates" : category.Trim(),
            Timeline = blueprint,
            Slots = slots,
        };
    }
}
