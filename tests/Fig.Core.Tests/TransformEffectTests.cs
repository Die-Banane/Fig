using System.Text.Json;
using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Tests;

public class KeyframeEasingTests
{
    [Fact]
    public void EasingCurves_StartAtZero_EndAtOne()
    {
        foreach (var easing in Enum.GetValues<KeyframeEasing>())
        {
            Assert.Equal(0, KeyframeEasingMath.Apply(easing, 0), 6);
            Assert.Equal(1, KeyframeEasingMath.Apply(easing, 1), 6);
        }
        Assert.Equal(0.25, KeyframeEasingMath.Apply(KeyframeEasing.EaseIn, 0.5), 6);
        Assert.Equal(0.75, KeyframeEasingMath.Apply(KeyframeEasing.EaseOut, 0.5), 6);
        Assert.Equal(0.5, KeyframeEasingMath.Apply(KeyframeEasing.EaseInOut, 0.5), 6);
        Assert.Equal(0, KeyframeEasingMath.Apply(KeyframeEasing.Hold, 0.99), 6);
    }

    [Fact]
    public void Evaluate_UsesTheEasingOfTheKeyframeItLeaves()
    {
        var track = new List<KeyframePoint>
        {
            new(0, ParamValue.OfDouble(0), KeyframeEasing.EaseIn),
            new(1, ParamValue.OfDouble(1)),
        };
        Assert.Equal(0.25, EffectPipeline.Evaluate(track, 0.5).AsDouble, 6);

        track[0] = track[0] with { Easing = KeyframeEasing.Hold };
        Assert.Equal(0, EffectPipeline.Evaluate(track, 0.5).AsDouble, 6);
    }

    [Fact]
    public void KeyframesSavedBeforeEasingExisted_LoadAsLinear()
    {
        var old = "{\"TimeSec\":1.5,\"Value\":0.7}";
        var point = JsonSerializer.Deserialize<KeyframePoint>(old);
        Assert.Equal(KeyframeEasing.Linear, point.Easing);
        Assert.Equal(1.5, point.TimeSec);

        var roundTrip = JsonSerializer.Deserialize<KeyframePoint>(
            JsonSerializer.Serialize(new KeyframePoint(2, ParamValue.OfDouble(1), KeyframeEasing.EaseOut)));
        Assert.Equal(KeyframeEasing.EaseOut, roundTrip.Easing);
    }

    [Fact]
    public void ReKeyingAValue_KeepsItsEasing()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("a", 0, 10);
        editor.AddClip(track.Id, clip);
        var fx = EffectCatalog.Find(EffectCatalog.Brightness)!.CreateInstance();
        editor.AddEffect(clip.Id, fx);
        editor.SetKeyframe(clip.Id, fx.Id, "amount", 1, ParamValue.OfDouble(0.2));
        editor.SetEffectKeyframeEasing(clip.Id, fx.Id, 1, KeyframeEasing.Hold);
        editor.SetKeyframe(clip.Id, fx.Id, "amount", 1, ParamValue.OfDouble(0.4));

        Assert.Equal(KeyframeEasing.Hold, fx.Keyframes["amount"][0].Easing);
        Assert.Equal(0.4, fx.Keyframes["amount"][0].Value.AsDouble, 6);
    }
}

public class TransformEffectTests
{
    private static DecodedFrame Gradient(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                px[i] = (byte)(x * 255 / (w - 1));
                px[i + 1] = (byte)(y * 255 / (h - 1));
                px[i + 3] = 255;
            }
        return new DecodedFrame { Width = w, Height = h, Pixels = px };
    }

    /// <summary>A whole-clip Transform animated with zoom keyframes (time, zoom).</summary>
    private static EffectInstance Keyed(params (double T, double Zoom)[] keys)
    {
        var fx = EffectCatalog.Find(EffectCatalog.Transform)!.CreateInstance();
        fx.Keyframes["zoom"] = keys.Select(k => new KeyframePoint(k.T, ParamValue.OfDouble(k.Zoom), KeyframeEasing.Linear)).ToList();
        return fx;
    }

    [Fact]
    public void Transform_CoversTheWholeClip_AndIsNumericallyKeyframable()
    {
        var entry = EffectCatalog.Find(EffectCatalog.Transform)!;
        Assert.Equal(new[] { "centerX", "centerY", "zoom", "rotation" }, entry.ParamSchema.Select(p => p.Key));

        var fx = entry.CreateInstance();
        Assert.False(fx.HasRange);
        Assert.True(fx.IsActiveAt(0));
        Assert.True(fx.IsActiveAt(123));
    }

    [Fact]
    public void ZoomInHoldAndOut_IsJustKeyframes_OnOneEffect()
    {
        var fx = Keyed((1, 1), (2, 2.5), (4, 2.5), (5, 1));

        Assert.Equal(1.0, TransformMath.ViewAt(fx, 1).Zoom, 3);
        Assert.Equal(1.75, TransformMath.ViewAt(fx, 1.5).Zoom, 3);   // zooming in
        Assert.Equal(2.5, TransformMath.ViewAt(fx, 3).Zoom, 3);      // holding
        Assert.Equal(1.75, TransformMath.ViewAt(fx, 4.5).Zoom, 3);   // smoothly back out
        Assert.Equal(1.0, TransformMath.ViewAt(fx, 5).Zoom, 3);
    }

    [Fact]
    public void KeyframesHoldTheirValues_BeforeTheFirstAndAfterTheLast()
    {
        var frame = Gradient(16, 16);
        var fx = Keyed((2, 2), (4, 3));                     // never returns to 1×

        Assert.Equal(2.0, TransformMath.ViewAt(fx, 0).Zoom, 3);     // before the first keyframe
        Assert.Equal(3.0, TransformMath.ViewAt(fx, 15).Zoom, 3);    // long after the last
        Assert.NotSame(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 0));
        Assert.NotSame(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 15));
    }

    [Fact]
    public void ApplyingTheStack_ZoomsTheFrame_AtAHeldKeyframe()
    {
        var frame = Gradient(16, 16);
        var fx = Keyed((1, 1), (2, 2), (4, 2), (5, 1));

        Assert.Same(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 1));      // zoom 1: untouched

        var held = EffectPipeline.ApplyStack(frame, new[] { fx }, 3);
        Assert.NotSame(frame, held);
        Assert.InRange(held.Pixels[0], 55, 80);                                     // top-left shows 1/4 of the way in
    }

    [Fact]
    public void AnEffectLimitedToPartOfTheClip_StopsWhereItsLimitEnds()
    {
        var frame = Gradient(16, 16);
        var fx = Keyed((0, 2), (10, 2));
        fx.StartSec = 2;
        fx.DurSec = 3;

        Assert.Same(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 1));
        Assert.NotSame(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 3));
        Assert.Same(frame, EffectPipeline.ApplyStack(frame, new[] { fx }, 6));
    }

    [Fact]
    public void TheCamera_NeverLeavesThePicture()
    {
        var view = TransformMath.ViewFrom(new Dictionary<string, ParamValue>
        {
            ["centerX"] = ParamValue.OfDouble(1), ["centerY"] = ParamValue.OfDouble(0), ["zoom"] = ParamValue.OfDouble(4),
        });
        Assert.Equal(0.875, view.CenterX, 3);
        Assert.Equal(0.125, view.CenterY, 3);
    }
}

public class EffectPresetTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fig-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Fact]
    public void EveryBuiltInPreset_BuildsAWholeClipEffect_WithKeyframes()
    {
        Assert.NotEmpty(BuiltInEffectPresets.All);
        foreach (var preset in BuiltInEffectPresets.All)
        {
            var fx = EffectPresetInstantiator.Create(preset, clipDurSec: 10, atLocalSec: 2);
            Assert.False(fx.HasRange);
            Assert.Equal(preset.TypeId, fx.TypeId);
            Assert.NotEmpty(fx.Keyframes["zoom"]);
        }
    }

    [Fact]
    public void ZoomInAndOut_HasFourKeyframes_AndReturnsToFullFrame()
    {
        var preset = BuiltInEffectPresets.Find("zoom-in-out")!;
        var fx = EffectPresetInstantiator.Create(preset, clipDurSec: 20, atLocalSec: 3);

        var zoom = fx.Keyframes["zoom"];
        Assert.Equal(4, zoom.Count);
        Assert.Equal(3, zoom[0].TimeSec, 6);
        Assert.Equal(3 + preset.DefaultDurationSec, zoom[^1].TimeSec, 6);
        Assert.Equal(1, zoom[0].Value.AsDouble);
        Assert.Equal(1, zoom[^1].Value.AsDouble);
        Assert.True(zoom[1].Value.AsDouble > 1.5);
    }

    [Fact]
    public void ZoomIn_StaysZoomedIn_ForTheRestOfTheClip()
    {
        var fx = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("zoom-in")!, 20, 2);

        Assert.Equal(1.0, TransformMath.ViewAt(fx, 0).Zoom, 3);
        Assert.Equal(2.0, TransformMath.ViewAt(fx, 5).Zoom, 3);
        Assert.Equal(2.0, TransformMath.ViewAt(fx, 19).Zoom, 3);
    }

    [Fact]
    public void AMotionThatDoesNotFit_IsShortenedAndMovedBack()
    {
        var preset = BuiltInEffectPresets.Find("zoom-in")!;
        var fx = EffectPresetInstantiator.Create(preset, clipDurSec: 2, atLocalSec: 1.5);

        Assert.Equal(0, fx.Keyframes["zoom"][0].TimeSec, 6);
        Assert.Equal(2, fx.Keyframes["zoom"][^1].TimeSec, 6);
    }

    [Fact]
    public void AnEffect_CanBeSavedAsAPreset_AndComesBackTheSame()
    {
        var original = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("zoom-in-out")!, 20, 4);
        var preset = EffectPresetInstantiator.FromEffect(original, 20, "My move");

        var store = new EffectPresetStore(_folder);
        store.Save(preset);
        var loaded = Assert.Single(store.List());
        Assert.Equal("My move", loaded.Name);
        Assert.False(loaded.BuiltIn);

        var rebuilt = EffectPresetInstantiator.Create(loaded, 20, 4);
        foreach (var t in new[] { 4.0, 5.0, 6.5, 7.5, 8.0 })
            Assert.Equal(TransformMath.ViewAt(original, t).Zoom, TransformMath.ViewAt(rebuilt, t).Zoom, 3);

        Assert.True(store.Delete(loaded.Id));
        Assert.Empty(store.List());
    }
}

public class EffectKeyframeEditingTests
{
    private static (TimelineEditor Editor, Clip Clip, EffectInstance Fx) Setup()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("a", 0, 20);
        editor.AddClip(track.Id, clip);
        var fx = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("zoom-in-out")!, 20, 4);
        editor.AddEffect(clip.Id, fx);
        return (editor, clip, fx);
    }

    private static (TimelineEditor Editor, Clip Clip, EffectInstance Fx) SetupStatic()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("a", 0, 20);
        editor.AddClip(track.Id, clip);
        var fx = EffectCatalog.Find(EffectCatalog.Transform)!.CreateInstance();
        editor.AddEffect(clip.Id, fx);
        return (editor, clip, fx);
    }

    [Fact]
    public void TurningOnAnimation_AddsOneKeyframe_HoldingTheCurrentLook()
    {
        var (editor, clip, fx) = SetupStatic();
        fx.Params["zoom"] = ParamValue.OfDouble(1.8);

        editor.SetEffectAnimated(clip.Id, fx.Id, true, 6);

        Assert.Equal(new[] { 6.0 }, fx.Keyframes["zoom"].Select(k => k.TimeSec).ToArray());
        Assert.Equal(1.8, TransformMath.ViewAt(fx, 0).Zoom, 3);       // nothing changes visibly...
        Assert.Equal(1.8, TransformMath.ViewAt(fx, 19).Zoom, 3);      // ...anywhere in the clip

        editor.Undo();
        Assert.Empty(fx.Keyframes);
    }

    [Fact]
    public void TurningOffAnimation_KeepsTheLookAtThePlayhead_AsTheConstantValue()
    {
        var (editor, clip, fx) = Setup();
        var expected = TransformMath.ViewAt(fx, 5).Zoom;

        editor.SetEffectAnimated(clip.Id, fx.Id, false, 5);

        Assert.Empty(fx.Keyframes);
        Assert.Equal(expected, TransformMath.ViewAt(fx, 0).Zoom, 3);
        Assert.Equal(expected, TransformMath.ViewAt(fx, 18).Zoom, 3);

        editor.Undo();
        Assert.NotEmpty(fx.Keyframes);
    }

    [Fact]
    public void TheFirstKeyframe_IsJustOneKeyframe_NotThree()
    {
        var (editor, clip, fx) = SetupStatic();

        var added = editor.AddEffectKeyframe(clip.Id, fx.Id, 3.5);

        Assert.Equal(3.5, added!.Value, 6);
        Assert.Equal(new[] { 3.5 }, fx.Keyframes["zoom"].Select(k => k.TimeSec).ToArray());
    }

    [Fact]
    public void AKeyframeCanBeAdded_AnywhereInTheClip()
    {
        var (editor, clip, fx) = SetupStatic();

        Assert.Equal(0, editor.AddEffectKeyframe(clip.Id, fx.Id, -4)!.Value, 6);        // clamped into the clip
        Assert.Equal(20, editor.AddEffectKeyframe(clip.Id, fx.Id, 99)!.Value, 6);
    }

    [Fact]
    public void AddingAKeyframeInTheMiddle_HoldsTheValueTheMotionHadThere()
    {
        var (editor, clip, fx) = Setup();
        var halfway = fx.Keyframes["zoom"][0].TimeSec + 0.5 * (fx.Keyframes["zoom"][1].TimeSec - fx.Keyframes["zoom"][0].TimeSec);
        var expected = TransformMath.ViewAt(fx, halfway).Zoom;

        editor.AddEffectKeyframe(clip.Id, fx.Id, halfway);

        Assert.Equal(expected, fx.Keyframes["zoom"].First(k => Math.Abs(k.TimeSec - halfway) < 1e-6).Value.AsDouble, 6);
    }

    [Fact]
    public void SettingAKeyframe_KeysEveryGivenParameterAtOnce()
    {
        var (editor, clip, fx) = Setup();
        editor.SetEffectKeyframe(clip.Id, fx.Id, 5.5, new Dictionary<string, double> { ["zoom"] = 3, ["centerX"] = 0.4 });

        Assert.Contains(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - 5.5) < 1e-6 && k.Value.AsDouble == 3);
        Assert.Contains(fx.Keyframes["centerX"], k => Math.Abs(k.TimeSec - 5.5) < 1e-6);
        Assert.True(fx.Keyframes["zoom"].SequenceEqual(fx.Keyframes["zoom"].OrderBy(k => k.TimeSec)));

        editor.Undo();
        Assert.DoesNotContain(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - 5.5) < 1e-6);
    }

    [Fact]
    public void DraggingAValue_IsOneUndoStep()
    {
        var (editor, clip, fx) = Setup();
        var before = fx.Keyframes["zoom"].Count;
        foreach (var z in new[] { 2.0, 2.4, 2.8, 3.2 })
            editor.SetEffectKeyframe(clip.Id, fx.Id, 5.5, new Dictionary<string, double> { ["zoom"] = z });

        Assert.Equal(before + 1, fx.Keyframes["zoom"].Count);
        editor.Undo();
        Assert.Equal(before, fx.Keyframes["zoom"].Count);
    }

    [Fact]
    public void DeletingAKeyframe_RemovesItFromEveryParameter()
    {
        var (editor, clip, fx) = Setup();
        var middle = fx.Keyframes["zoom"][1].TimeSec;
        editor.RemoveEffectKeyframe(clip.Id, fx.Id, middle);

        Assert.All(fx.Keyframes.Values, t => Assert.DoesNotContain(t, k => Math.Abs(k.TimeSec - middle) < 1e-6));
        editor.Undo();
        Assert.Contains(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - middle) < 1e-6);
    }

    [Fact]
    public void MovingAKeyframe_MovesItOnEveryParameter_AndADragUndoesTogether()
    {
        var (editor, clip, fx) = Setup();
        var from = fx.Keyframes["zoom"][1].TimeSec;

        editor.MoveEffectKeyframe(clip.Id, fx.Id, from, from + 0.2, "drag1");
        editor.MoveEffectKeyframe(clip.Id, fx.Id, from + 0.2, from + 0.5, "drag1");

        Assert.All(fx.Keyframes.Values, t => Assert.Contains(t, k => Math.Abs(k.TimeSec - (from + 0.5)) < 1e-6));
        editor.Undo();
        Assert.All(fx.Keyframes.Values, t => Assert.Contains(t, k => Math.Abs(k.TimeSec - from) < 1e-6));
    }

    [Fact]
    public void DuplicatingAKeyframe_CopiesItsValuesAndEasing()
    {
        var (editor, clip, fx) = Setup();
        var source = fx.Keyframes["zoom"][1];
        editor.SetEffectKeyframeEasing(clip.Id, fx.Id, source.TimeSec, KeyframeEasing.Hold);

        editor.DuplicateEffectKeyframe(clip.Id, fx.Id, source.TimeSec, 12);

        var copy = fx.Keyframes["zoom"].First(k => Math.Abs(k.TimeSec - 12) < 1e-6);
        Assert.Equal(source.Value.AsDouble, copy.Value.AsDouble, 6);
        Assert.Equal(KeyframeEasing.Hold, copy.Easing);
        Assert.Equal(source.TimeSec, fx.Keyframes["zoom"][1].TimeSec, 6);          // the original stays
    }

    [Fact]
    public void LimitingAnEffectToPartOfTheClip_DoesNotMoveItsKeyframes()
    {
        var (editor, clip, fx) = Setup();
        var firstKey = fx.Keyframes["zoom"][0].TimeSec;

        editor.SetEffectRange(clip.Id, fx.Id, 3, 8);
        Assert.Equal(3, fx.StartSec!.Value, 6);
        Assert.Equal(firstKey, fx.Keyframes["zoom"][0].TimeSec, 6);

        editor.SetEffectRange(clip.Id, fx.Id, null, null);                         // back to the whole clip
        Assert.False(fx.HasRange);
    }

    [Fact]
    public void ChangingEasing_AppliesToTheWholeKeyframe()
    {
        var (editor, clip, fx) = Setup();
        var t = fx.Keyframes["zoom"][0].TimeSec;
        editor.SetEffectKeyframeEasing(clip.Id, fx.Id, t, KeyframeEasing.Hold);

        Assert.All(fx.Keyframes.Values, track => Assert.Equal(KeyframeEasing.Hold, track.First(k => Math.Abs(k.TimeSec - t) < 1e-6).Easing));
    }
}

public class EffectMigrationTests
{
    private static Project.Project ProjectWith(Clip clip)
    {
        var timeline = new TimelineModel { Rate = new FrameRate(30, 1) };
        timeline.Tracks.Add(new Track { Kind = TrackKind.Video, Clips = { clip } });
        return new Project.Project { Timelines = { timeline } };
    }

    [Fact]
    public void SavedZoomRegions_BecomeAWholeClipTransformWithTwoKeyframes()
    {
        var clip = new VideoClip { Id = "v", DurSec = 12 };
        clip.Effects.Add(new EffectInstance
        {
            TypeId = "zoomRegion",
            StartSec = 2,
            DurSec = 3,
            Params =
            {
                ["startX"] = ParamValue.OfDouble(0.5), ["startY"] = ParamValue.OfDouble(0.5), ["startZoom"] = ParamValue.OfDouble(1),
                ["endX"] = ParamValue.OfDouble(0.75), ["endY"] = ParamValue.OfDouble(0.75), ["endZoom"] = ParamValue.OfDouble(2.5),
                ["ease"] = ParamValue.OfChoice(3),
            },
        });
        var project = ProjectWith(clip);

        Assert.Equal(1, EffectMigration.Migrate(project));

        var fx = clip.Effects[0];
        Assert.Equal(EffectCatalog.Transform, fx.TypeId);
        Assert.False(fx.HasRange);
        Assert.Equal(2, fx.Keyframes["zoom"].Count);
        Assert.Equal(1.0, TransformMath.ViewAt(fx, 2).Zoom, 3);
        Assert.Equal(2.5, TransformMath.ViewAt(fx, 5).Zoom, 3);
        Assert.Equal(2.5, TransformMath.ViewAt(fx, 11).Zoom, 3);          // holds, instead of snapping back
        Assert.Equal(0, EffectMigration.Migrate(project));                 // nothing left to change
    }

    [Fact]
    public void AnimatedTransformsThatWereLimitedToABar_BecomeWholeClip_SoTheyStopSnappingBack()
    {
        var clip = new VideoClip { Id = "v", DurSec = 20 };
        var fx = EffectPresetInstantiator.Create(BuiltInEffectPresets.Find("zoom-in")!, 20, 2);
        fx.StartSec = 2;
        fx.DurSec = 3;
        clip.Effects.Add(fx);

        Assert.Equal(1, EffectMigration.Migrate(ProjectWith(clip)));

        Assert.False(fx.HasRange);
        Assert.Equal(2.0, TransformMath.ViewAt(fx, 18).Zoom, 3);
    }

    [Fact]
    public void OtherLimitedEffects_KeepTheirLimit()
    {
        var clip = new VideoClip { Id = "v", DurSec = 20 };
        var vignette = EffectCatalog.Find(EffectCatalog.Vignette)!.CreateInstance();
        vignette.StartSec = 2;
        vignette.DurSec = 3;
        clip.Effects.Add(vignette);

        Assert.Equal(0, EffectMigration.Migrate(ProjectWith(clip)));
        Assert.True(vignette.HasRange);
    }
}
