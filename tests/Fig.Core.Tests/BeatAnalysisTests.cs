using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class BeatAnalysisTests
{
    [Fact]
    public void AttackSensitivity_IncludesWeakerHits_WithoutDoubleMarkingAnAttack()
    {
        double[] strengths = new double[300];
        strengths[25] = 10;
        strengths[30] = 5;
        strengths[100] = 3;
        strengths[200] = 1;
        var low = BeatAnalysisService.PickAttacks(strengths, 100, 10, 0);
        var high = BeatAnalysisService.PickAttacks(strengths, 100, 10, 1);
        Assert.Equal(new[] { 10.25 }, low);
        Assert.Equal(new[] { 10.25, 11.0, 12.0 }, high);
    }

    [Fact]
    public void BeatSensitivity_FiltersWeakBeats()
    {
        double[] strengths = new double[300];
        strengths[25] = 10;
        strengths[100] = 2;
        Assert.Equal(new[] { 0.25 }, BeatAnalysisService.FilterBeatStrengths([0.25, 1], strengths, 100, 0, 0));
        Assert.Equal(new[] { 0.25, 1.0 }, BeatAnalysisService.FilterBeatStrengths([0.25, 1], strengths, 100, 0, 1));
    }

    [Fact]
    public void Regeneration_ReplacesGeneratedMarkers_AndUndoRestoresThem()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = new AudioClip { DurSec = 3 };
        editor.AddClip(track.Id, clip);
        var manual = new Marker { StartSec = 0.1 };
        clip.Markers.Add(manual);
        var first = editor.AddBeatMarkers(clip, [0.5, 1.0]).ToArray();
        var second = editor.AddBeatMarkers(clip, [2.0]);
        Assert.Equal(2, clip.Markers.Count);
        Assert.Contains(manual, clip.Markers);
        editor.Undo();
        Assert.Equal(new[] { manual }.Concat(first), clip.Markers);
        editor.Redo();
        Assert.Equal(new[] { manual }.Concat(second), clip.Markers);
        editor.AddBeatMarkers(clip, []);
        Assert.Same(manual, Assert.Single(clip.Markers));
    }

    [Fact]
    public void Tracker_Follows120BpmAttacks_AndPreservesSourceOffset()
    {
        double[] onsets = new double[1000];
        for (int i = 25; i < onsets.Length; i += 50)
            onsets[i] = 10;

        var beats = BeatAnalysisService.TrackBeats(onsets, 100, 12);

        Assert.Equal(20, beats.Count);
        for (int i = 0; i < beats.Count; i++)
            Assert.Equal(12.25 + i * 0.5, beats[i], 6);
    }

    [Fact]
    public void Tracker_SilenceHasNoBeats()
    {
        Assert.Empty(BeatAnalysisService.TrackBeats(new double[1000], 100));
    }

    [Fact]
    public void BeatMarkers_RespectClipBounds_AndUndoAsOneBatch()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = new AudioClip { StartSec = 20, DurSec = 3, SrcInSec = 10, SrcOutSec = 13 };
        editor.AddClip(track.Id, clip);
        var original = new Marker { StartSec = 1, Name = "Existing" };
        clip.Markers.Add(original);

        var markers = editor.AddBeatMarkers(clip, [-1, 0, 0.5, 0.5, 2.5, 3, double.NaN]);
        Assert.Equal(new[] { 0.0, 0.5, 2.5 }, markers.Select(m => m.StartSec));
        var ids = markers.Select(m => m.Id).ToArray();
        Assert.Equal(4, clip.Markers.Count);

        editor.Undo();
        Assert.Same(original, Assert.Single(clip.Markers));
        editor.Redo();
        Assert.Equal(ids, clip.Markers.Skip(1).Select(m => m.Id));
    }

    [Fact]
    public void Analysis_ReselectsFromTheSameResult_AsSensitivityMoves()
    {
        double[] onsets = new double[300];
        onsets[25] = 10;
        onsets[100] = 2;
        double[] kicks = new double[300];
        kicks[25] = 10;
        kicks[30] = 5;
        kicks[100] = 3;
        var analysis = new BeatAnalysis(onsets, kicks, [0.25, 1], 100, 0);

        Assert.Equal(new[] { 0.25 }, analysis.Select(0, false));
        Assert.Equal(new[] { 0.25, 1.0 }, analysis.Select(1, false));
        Assert.True(analysis.Select(1, true).Count >= analysis.Select(0, true).Count);
        Assert.Equal(BeatAnalysisService.PickAttacks(kicks, 100, 0, 0.5), analysis.Select(0.5, true));
    }

    [Fact]
    public void Preview_IsOutsideTheDocument_AndCommitIsOneUndoStep()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = new AudioClip { DurSec = 4 };
        editor.AddClip(track.Id, clip);
        var generated = editor.AddBeatMarkers(clip, [1.0]).ToArray();
        int changes = 0, previews = 0;
        editor.TimelineChanged += () => changes++;
        editor.BeatPreviewChanged += () => previews++;

        editor.SetBeatPreview(clip, [0.5, 2.0, 2.0, 9]);
        editor.SetBeatPreview(clip, [0.5, 2.0]);

        Assert.Equal(0, changes);
        Assert.Equal(2, previews);
        Assert.Equal(generated, clip.Markers);
        Assert.Equal(new[] { 0.5, 2.0 }, editor.BeatPreview!.LocalTimes);
        Assert.True(editor.IsSupersededByPreview(clip, generated[0]));
        Assert.False(editor.IsSupersededByPreview(clip, new Marker()));

        var committed = editor.CommitBeatPreview();
        Assert.Null(editor.BeatPreview);
        Assert.Equal(new[] { 0.5, 2.0 }, clip.Markers.Select(m => m.StartSec));
        Assert.Equal(2, committed.Count);

        editor.Undo();
        Assert.Equal(generated, clip.Markers);
    }

    [Fact]
    public void Preview_IsDroppedWhenItsClipIsRemoved_AndClearIsIdempotent()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = new AudioClip { DurSec = 4 };
        editor.AddClip(track.Id, clip);
        editor.SetBeatPreview(clip, [1]);

        editor.RippleDelete(clip.Id);

        Assert.Null(editor.BeatPreview);
        int previews = 0;
        editor.BeatPreviewChanged += () => previews++;
        editor.ClearBeatPreview();
        Assert.Equal(0, previews);
        Assert.Empty(editor.CommitBeatPreview());
    }
}
