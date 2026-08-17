using Fig.Core.Media;
using Fig.Core.Templates;
using Fig.Core.Timeline;
using Xunit;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Tests;

public sealed class TemplateTests
{
    [Fact]
    public void Build_replaces_media_references_with_reusable_slots()
    {
        var asset = Asset("source-a", MediaKind.Video, 8);
        var timeline = TimelineWith(new VideoClip
        {
            SourceId = asset.Id,
            StartSec = 0,
            DurSec = 3,
            SrcInSec = 1,
            SrcOutSec = 4,
        });

        var template = TemplateBuilder.FromTimeline("Quick cut", "Social", timeline, [asset]);

        var slot = Assert.Single(template.Slots);
        var clip = Assert.IsType<VideoClip>(Assert.Single(template.Timeline.Tracks[0].Clips));
        Assert.Equal(slot.Id, clip.SourceId);
        Assert.Equal(3, slot.TargetDurationSec);
        Assert.Equal("Quick cut", template.Name);
    }

    [Fact]
    public void Instantiate_maps_slots_and_renews_identity()
    {
        var source = Asset("source-a", MediaKind.Video, 8);
        var replacement = Asset("replacement", MediaKind.Video, 12);
        var timeline = TimelineWith(new VideoClip
        {
            SourceId = source.Id,
            LinkGroupId = "linked",
            DurSec = 3,
            SrcOutSec = 3,
        });
        var template = TemplateBuilder.FromTimeline("Quick cut", "Social", timeline, [source]);
        var slot = Assert.Single(template.Slots);

        var instance = TemplateInstantiator.Instantiate(template,
            new Dictionary<string, MediaAsset> { [slot.Id] = replacement });

        var clip = Assert.IsType<VideoClip>(Assert.Single(instance.Tracks[0].Clips));
        Assert.Equal(replacement.Id, clip.SourceId);
        Assert.NotEqual(template.Timeline.Id, instance.Id);
        Assert.NotEqual(template.Timeline.Tracks[0].Id, instance.Tracks[0].Id);
        Assert.NotEqual(template.Timeline.Tracks[0].Clips[0].Id, clip.Id);
        Assert.NotEqual("linked", clip.LinkGroupId);
    }

    [Fact]
    public void Validation_reports_missing_and_wrong_kind_but_accepts_short_media()
    {
        var source = Asset("source-a", MediaKind.Video, 8);
        var timeline = TimelineWith(new VideoClip
        {
            SourceId = source.Id,
            DurSec = 5,
            SrcOutSec = 5,
        });
        var template = TemplateBuilder.FromTimeline("Quick cut", "Social", timeline, [source]);
        var slot = Assert.Single(template.Slots);

        Assert.Single(TemplateInstantiator.ValidateAssignments(template,
            new Dictionary<string, MediaAsset>()));

        var errors = TemplateInstantiator.ValidateAssignments(template,
            new Dictionary<string, MediaAsset> { [slot.Id] = Asset("audio", MediaKind.Audio, 2) });
        Assert.Single(errors);

        Assert.Empty(TemplateInstantiator.ValidateAssignments(template,
            new Dictionary<string, MediaAsset> { [slot.Id] = Asset("short-video", MediaKind.Video, 0.5) }));
    }

    [Fact]
    public void Instantiate_keeps_template_timing_when_replacement_is_shorter()
    {
        var source = Asset("source-a", MediaKind.Video, 8);
        var template = TemplateBuilder.FromTimeline("Quick cut", "Social",
            TimelineWith(new VideoClip
            {
                SourceId = source.Id,
                DurSec = 5,
                SrcOutSec = 5,
            }), [source]);
        var slot = Assert.Single(template.Slots);

        var instance = TemplateInstantiator.Instantiate(template,
            new Dictionary<string, MediaAsset> { [slot.Id] = Asset("short-video", MediaKind.Video, 0.5) });

        var clip = Assert.IsType<VideoClip>(Assert.Single(instance.Tracks[0].Clips));
        Assert.Equal("short-video", clip.SourceId);
        Assert.Equal(5, clip.DurSec);
    }

    [Fact]
    public void Store_round_trips_template_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fig-templates-{Guid.NewGuid():N}");
        try
        {
            var template = TemplateBuilder.FromTimeline("Saved edit", "My templates",
                TimelineWith(new TextClip { Text = "Title", DurSec = 2 }), []);
            var store = new TemplateStore(root);

            store.Save(template);

            var loaded = Assert.Single(store.List());
            Assert.Equal(template.Id, loaded.Id);
            Assert.Equal("Saved edit", loaded.Name);
            Assert.IsType<TextClip>(Assert.Single(loaded.Timeline.Tracks[0].Clips));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static MediaAsset Asset(string id, MediaKind kind, double duration) => new()
    {
        Id = id,
        Kind = kind,
        DurationSec = duration,
        Url = $"/{id}.mp4",
    };

    private static TimelineModel TimelineWith(Clip clip)
    {
        var timeline = new TimelineModel { Rate = FrameRate.Common(30) };
        timeline.Tracks.Add(new Track
        {
            Kind = clip is AudioClip ? TrackKind.Audio : TrackKind.Video,
            Clips = [clip],
        });
        return timeline;
    }
}
