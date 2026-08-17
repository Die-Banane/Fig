using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Templates;

public static class BuiltInTemplateCatalog
{
    public static IReadOnlyList<VideoTemplate> All { get; } =
    [
        Create("clean-opener", "Clean opener", "A measured three-shot opener for channels and product stories.",
            "YouTube", 1920, 1080, [2.5, 2.5, 3.0]),
        Create("vertical-recap", "Vertical recap", "Six quick replacements paced for a short social recap.",
            "Social", 1080, 1920, [1.2, 1.2, 1.2, 1.2, 1.2, 1.5]),
        Create("product-beats", "Product beats", "Four deliberate product shots with soft cross-dissolves.",
            "Business", 1920, 1080, [2.0, 2.0, 2.0, 2.5], transitions: true),
    ];

    private static VideoTemplate Create(
        string id,
        string name,
        string description,
        string category,
        int width,
        int height,
        IReadOnlyList<double> durations,
        bool transitions = false)
    {
        var timeline = new TimelineModel { Rate = FrameRate.Common(30) };
        var track = new Track { Kind = TrackKind.Video, Name = "Template video" };
        timeline.Tracks.Add(track);
        var slots = new List<TemplateSlot>();
        double start = 0;

        for (var i = 0; i < durations.Count; i++)
        {
            var slot = new TemplateSlot
            {
                Id = $"{id}-slot-{i + 1}",
                Name = $"Shot {i + 1}",
                Kind = MediaKind.Video,
                TargetDurationSec = durations[i],
            };
            slots.Add(slot);
            var clip = new VideoClip
            {
                SourceId = slot.Id,
                StartSec = start,
                DurSec = durations[i],
                SrcOutSec = durations[i],
            };
            if (transitions && i > 0)
                clip.TransitionIn = new TransitionRef
                {
                    TypeId = TransitionCatalog.CrossDissolve,
                    DurationSec = 0.35,
                };
            track.Clips.Add(clip);
            start += durations[i];
        }

        return new VideoTemplate
        {
            Id = id,
            Name = name,
            Description = description,
            Category = category,
            CanvasWidth = width,
            CanvasHeight = height,
            IsBuiltIn = true,
            Timeline = timeline,
            Slots = slots,
        };
    }
}
