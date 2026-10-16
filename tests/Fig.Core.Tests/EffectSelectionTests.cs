using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class EffectSelectionTests
{
    private static (TimelineEditor Editor, Track Track, List<EffectInstance> Effects) Setup(params (double Start, double Dur)[] bars)
    {
        var (editor, track) = TimelineFixtures.Create();
        var effects = new List<EffectInstance>();
        foreach (var (start, dur) in bars)
        {
            var fx = EffectCatalog.Find(EffectCatalog.Brightness)!.CreateInstance();
            fx.StartSec = start;
            fx.DurSec = dur;
            editor.AddEffect(track.Id, fx);
            effects.Add(fx);
        }
        return (editor, track, effects);
    }

    private static (string, string)[] Items(Track track, IEnumerable<EffectInstance> effects)
        => effects.Select(e => (track.Id, e.Id)).ToArray();

    [Fact]
    public void Effects_CanBeAddedToAndTakenOutOfTheSelection_AndTheLastOneIsPrimary()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1), (5, 1));
        var selection = editor.Selection;

        selection.SelectEffect(track.Id, fx[0].Id);
        selection.ToggleEffect(track.Id, fx[1].Id);
        selection.ToggleEffect(track.Id, fx[2].Id);
        Assert.Equal(3, selection.SelectedEffects.Count);
        Assert.Equal(fx[2].Id, selection.SelectedEffect!.Value.EffectId);
        Assert.True(selection.IsEffectSelected(fx[1].Id));

        selection.ToggleEffect(track.Id, fx[1].Id);
        Assert.False(selection.IsEffectSelected(fx[1].Id));
        Assert.Equal(new[] { fx[0].Id, fx[2].Id }, selection.SelectedEffects.Select(e => e.EffectId));

        selection.SelectEffect(track.Id, fx[1].Id);
        Assert.Single(selection.SelectedEffects);
    }

    [Fact]
    public void SelectingAnythingElse_DropsTheEffectSelection()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1));
        editor.Selection.SelectEffects(Items(track, fx));
        Assert.Equal(2, editor.Selection.SelectedEffects.Count);

        editor.Selection.SelectOnly("some-clip");
        Assert.Empty(editor.Selection.SelectedEffects);
        Assert.Null(editor.Selection.SelectedEffect);

        editor.Selection.SelectEffects(Items(track, fx));
        editor.Selection.Clear();
        Assert.Empty(editor.Selection.SelectedEffects);
    }

    [Fact]
    public void SelectEffects_Additive_KeepsWhatWasAlreadySelected()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1), (5, 1));
        editor.Selection.SelectEffect(track.Id, fx[0].Id);
        editor.Selection.SelectEffects(Items(track, fx.Skip(1)), additive: true);
        Assert.Equal(3, editor.Selection.SelectedEffects.Count);
    }

    [Fact]
    public void RemoveEffects_IsOneUndoStep()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1), (5, 1));

        Assert.Equal(2, editor.RemoveEffects(Items(track, fx.Take(2))));
        Assert.Equal(new[] { fx[2].Id }, track.Effects.Select(e => e.Id));

        editor.Undo();
        Assert.Equal(new[] { fx[0].Id, fx[1].Id, fx[2].Id }, track.Effects.Select(e => e.Id));
        editor.Redo();
        Assert.Equal(new[] { fx[2].Id }, track.Effects.Select(e => e.Id));
    }

    [Fact]
    public void MoveEffects_SlidesTheBarsTogether_AndAWholeDragIsOneUndoStep()
    {
        var (editor, track, fx) = Setup((2, 1), (4, 2), (9, 1));
        fx[1].Keyframes["amount"] = new List<KeyframePoint> { new(4, ParamValue.OfDouble(0)), new(6, ParamValue.OfDouble(1)) };
        var items = Items(track, fx);

        // the same drag, reported three times with the total distance so far
        editor.MoveEffects(items, 0.5, "drag");
        editor.MoveEffects(items, 1.0, "drag");
        editor.MoveEffects(items, 1.5, "drag");

        Assert.Equal(new[] { 3.5, 5.5, 10.5 }, fx.Select(e => e.StartSec!.Value));
        Assert.Equal(new[] { 1.0, 2.0, 1.0 }, fx.Select(e => e.DurSec!.Value)); // lengths untouched
        Assert.Equal(new[] { 5.5, 7.5 }, fx[1].Keyframes["amount"].Select(k => k.TimeSec)); // keyframes ride along

        editor.Undo();
        Assert.Equal(new[] { 2.0, 4.0, 9.0 }, fx.Select(e => e.StartSec!.Value));
        Assert.Equal(new[] { 4.0, 6.0 }, fx[1].Keyframes["amount"].Select(k => k.TimeSec));

        editor.Redo();
        Assert.Equal(new[] { 3.5, 5.5, 10.5 }, fx.Select(e => e.StartSec!.Value));
    }

    [Fact]
    public void MoveEffects_NeverPushesTheGroupBeforeTimeZero_OrChangesItsSpacing()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1), (6, 1));
        editor.MoveEffects(Items(track, fx), -10, "drag");
        // the earliest bar stops at zero and the others keep their distance from it
        Assert.Equal(new[] { 0.0, 2.0, 5.0 }, fx.Select(e => e.StartSec!.Value));
    }

    [Fact]
    public void MoveEffects_AFreshDragIsANewUndoStep()
    {
        var (editor, track, fx) = Setup((1, 1), (3, 1));
        var items = Items(track, fx);
        editor.MoveEffects(items, 1, "first");
        editor.MoveEffects(items, 1, "second"); // a different gesture: starts from where the first left them
        Assert.Equal(new[] { 3.0, 5.0 }, fx.Select(e => e.StartSec!.Value));

        editor.Undo();
        Assert.Equal(new[] { 2.0, 4.0 }, fx.Select(e => e.StartSec!.Value));
        editor.Undo();
        Assert.Equal(new[] { 1.0, 3.0 }, fx.Select(e => e.StartSec!.Value));
    }
}
