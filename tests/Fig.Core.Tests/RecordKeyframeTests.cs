using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Tests;

/// <summary>Recording a keyframe (the stopwatch) only ever adds keyframes.</summary>
public class RecordKeyframeTests
{
    private static (TimelineEditor Editor, VideoClip Clip, EffectInstance Fx) Setup()
    {
        var track = new Track { Kind = TrackKind.Video };
        var clip = new VideoClip { Id = "c", StartSec = 0, DurSec = 10, SrcOutSec = 10 };
        track.Clips.Add(clip);
        var editor = new TimelineEditor(new TimelineModel { Tracks = { track } });
        var fx = EffectCatalog.Find(EffectCatalog.Brightness)!.CreateInstance();
        editor.AddEffect("c", fx);
        return (editor, clip, fx);
    }

    private static List<double> Times(EffectInstance fx)
        => fx.Keyframes.Values.SelectMany(t => t).Select(p => p.TimeSec).Distinct().OrderBy(t => t).ToList();

    [Fact]
    public void Recording_on_a_fresh_effect_starts_the_animation_with_one_keyframe()
    {
        var (editor, _, fx) = Setup();
        Assert.Equal(2.0, editor.RecordEffectKeyframe("c", fx.Id, 2.0));
        Assert.Equal([2.0], Times(fx));
    }

    [Fact]
    public void Recording_again_keeps_every_earlier_keyframe()
    {
        var (editor, _, fx) = Setup();
        editor.RecordEffectKeyframe("c", fx.Id, 1.0);
        editor.RecordEffectKeyframe("c", fx.Id, 4.0);
        editor.RecordEffectKeyframe("c", fx.Id, 7.0);
        Assert.Equal([1.0, 4.0, 7.0], Times(fx));
    }

    [Fact]
    public void Recording_on_an_existing_keyframe_changes_nothing_and_adds_no_undo_step()
    {
        var (editor, _, fx) = Setup();
        editor.RecordEffectKeyframe("c", fx.Id, 3.0);
        editor.RecordEffectKeyframe("c", fx.Id, 3.0);
        Assert.Equal([3.0], Times(fx));
        editor.History.Undo();   // one step: the second press did not add another
        Assert.Empty(fx.Keyframes);
    }

    [Fact]
    public void A_recorded_keyframe_holds_the_look_at_that_time_and_undoes_alone()
    {
        var (editor, _, fx) = Setup();
        editor.RecordEffectKeyframe("c", fx.Id, 1.0);
        editor.SetEffectKeyframe("c", fx.Id, 5.0, new Dictionary<string, double> { ["amount"] = 0.5 });
        var lookAt3 = EffectPipeline.ResolveParams(fx, 3.0)["amount"].AsNumber;

        editor.RecordEffectKeyframe("c", fx.Id, 3.0);
        Assert.Equal(lookAt3, EffectPipeline.ResolveParams(fx, 3.0)["amount"].AsNumber, 6);
        Assert.Equal([1.0, 3.0, 5.0], Times(fx));

        editor.History.Undo();
        Assert.Equal([1.0, 5.0], Times(fx));
    }

    [Fact]
    public void Clearing_the_animation_is_a_separate_explicit_edit()
    {
        var (editor, _, fx) = Setup();
        editor.RecordEffectKeyframe("c", fx.Id, 1.0);
        editor.RecordEffectKeyframe("c", fx.Id, 4.0);
        editor.SetEffectAnimated("c", fx.Id, false, 2.0);
        Assert.Empty(fx.Keyframes);
        editor.History.Undo();
        Assert.Equal([1.0, 4.0], Times(fx));
    }
}
