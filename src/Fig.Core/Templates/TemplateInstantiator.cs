using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Templates;

public static class TemplateInstantiator
{
    public static IReadOnlyList<string> ValidateAssignments(
        VideoTemplate template,
        IReadOnlyDictionary<string, MediaAsset> assignments)
    {
        var errors = new List<string>();
        foreach (var slot in template.Slots)
        {
            if (!assignments.TryGetValue(slot.Id, out var asset))
            {
                if (slot.Required)
                    errors.Add($"{slot.Name} needs media.");
                continue;
            }
            if (!Compatible(slot.Kind, asset.Kind))
                errors.Add($"{slot.Name} expects {slot.Kind.ToString().ToLowerInvariant()} media.");
        }
        return errors;
    }

    public static TimelineModel Instantiate(
        VideoTemplate template,
        IReadOnlyDictionary<string, MediaAsset> assignments)
    {
        var errors = ValidateAssignments(template, assignments);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var timeline = TemplateTimeline.Clone(template.Timeline, freshIds: true);
        foreach (var clip in timeline.Tracks.SelectMany(track => track.Clips))
        {
            switch (clip)
            {
                case VideoClip video when assignments.TryGetValue(video.SourceId, out var videoAsset):
                    video.SourceId = videoAsset.Id;
                    video.SourceRate = videoAsset.SourceRate;
                    break;
                case AudioClip audio when assignments.TryGetValue(audio.SourceId, out var audioAsset):
                    audio.SourceId = audioAsset.Id;
                    audio.SourceRate = audioAsset.SourceRate;
                    break;
            }
        }
        return timeline;
    }

    private static bool Compatible(MediaKind expected, MediaKind actual) =>
        expected == actual || (expected == MediaKind.Video && actual == MediaKind.Image);
}
