using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class TrackEffectsTests
{
    [Fact]
    public void TrackRegionSpansCutsAndDoesNotFollowClipMovesOrDeletes()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("video", 5, 4);
        editor.AddClip(track.Id, clip);
        var effect = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("shake-hit")!, 20, 6);
        editor.AddEffect(track.Id, effect);
        editor.SetEffectRange(track.Id, effect.Id, 4, 8);
        var keys = effect.Keyframes.Values.SelectMany(k => k).Select(k => k.TimeSec).ToArray();
        editor.Cut(clip.Id, 7);
        Assert.Equal(2, track.Clips.Count);
        Assert.All(track.Clips, c => Assert.Empty(c.Effects));
        Assert.Same(effect, Assert.Single(track.Effects));
        Assert.Equal(4, effect.StartSec);
        Assert.Equal(8, effect.DurSec);
        editor.Move(track.Clips[0].Id, 20);
        Assert.Equal(4, effect.StartSec);
        editor.Selection.SelectClips(track.Clips.Select(c => c.Id));
        editor.LiftSelected();
        Assert.Empty(track.Clips);
        Assert.Same(effect, Assert.Single(track.Effects));
        Assert.Equal(keys, effect.Keyframes.Values.SelectMany(k => k).Select(k => k.TimeSec));
    }

    [Fact]
    public void TrackRangesAndKeyframesExtendBeyondClipEndsAndUndo()
    {
        var (editor, track) = TimelineFixtures.Create();
        editor.AddClip(track.Id, TimelineFixtures.Video("video", 0, 2));
        var effect = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("shake-hit")!, 10, 1);
        editor.AddEffect(track.Id, effect);
        editor.SetEffectRange(track.Id, effect.Id, 10, 4, moveKeyframes: true);
        Assert.Equal(10, effect.StartSec);
        Assert.Equal(4, effect.DurSec);
        Assert.Equal(10, effect.Keyframes.Values.SelectMany(keys => keys).Min(key => key.TimeSec));
        editor.Undo();
        Assert.Equal(1, effect.StartSec);
        editor.MoveEffectKeyframe(track.Id, effect.Id, 1, 15, "move");
        Assert.Contains(effect.Keyframes.Values.SelectMany(keys => keys), key => key.TimeSec == 15);
        editor.Undo();
        Assert.Contains(effect.Keyframes.Values.SelectMany(keys => keys), key => key.TimeSec == 1);
    }

    [Fact]
    public void TrackEffectsDuplicateToEmptyTrackWithoutClipBounds()
    {
        var (editor, source) = TimelineFixtures.Create();
        var target = new Track { Kind = TrackKind.Video };
        editor.Document.Tracks.Add(target);
        var effect = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("shake-hit")!, 10, 2);
        editor.AddEffect(source.Id, effect);
        var copy = editor.DuplicateEffectAt(source.Id, effect.Id, target.Id, 10)!;
        Assert.Equal(12, copy.StartSec);
        Assert.Equal(effect.DurSec, copy.DurSec);
        Assert.Single(source.Effects);
        Assert.Single(target.Effects);
        Assert.NotEqual(effect.Id, copy.Id);
        editor.Undo();
        Assert.Empty(target.Effects);
    }

    [Fact]
    public void OldClipEffectsMigrateOnceAndSurviveSaveLoad()
    {
        string root = Path.Combine(Path.GetTempPath(), "fig-track-effects-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(root);
            var project = store.LoadProject(store.CreateProject("Track effects"))!;
            var track = new Track { Kind = TrackKind.Video };
            project.Timelines.Add(new Fig.Core.Timeline.Timeline { Rate = FrameRate.Common(30) });
            project.Timelines[0].Tracks.Add(track);
            var clip = TimelineFixtures.Video("video", 8, 4);
            var effect = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("shake-hit")!, 4, 1);
            clip.Effects.Add(effect);
            track.Clips.Add(clip);
            store.SaveProject(project);
            var loaded = store.LoadProject(project.Id)!;
            var migrated = loaded.Timelines[0].Tracks.Single();
            Assert.Empty(migrated.Clips.Single().Effects);
            var region = Assert.Single(migrated.Effects);
            Assert.Equal(effect.Id, region.Id);
            Assert.Equal(9, region.StartSec);
            Assert.Equal(9, region.Keyframes.Values.SelectMany(keys => keys).Min(key => key.TimeSec));
            Assert.Equal(0, EffectMigration.MoveClipEffectsToTracks(loaded));
            store.SaveProject(loaded);
            Assert.Single(store.LoadProject(project.Id)!.Timelines[0].Tracks.Single().Effects);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
