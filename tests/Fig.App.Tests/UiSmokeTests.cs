using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fig.App.ViewModels;
using Fig.App.Views;
using Fig.Core.Input;
using Fig.Core.Project;

[assembly: AvaloniaTestApplication(typeof(Fig.App.Tests.TestAppBuilder))]

namespace Fig.App.Tests;

/// <summary>The real app's theme and shared control styles, without its Dev Tools attachment.</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Fig.App/"))
        {
            Source = new Uri("avares://Fig.App/Styles/FigControls.axaml"),
        });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Loads each top-level view in a headless window so XAML/runtime binding errors fail a test
/// instead of crashing the app. Set FIG_SHOT_DIR to also write PNG screenshots.
/// </summary>
public sealed class UiSmokeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fig-ui-" + Guid.NewGuid().ToString("N"));

    public UiSmokeTests() => Directory.CreateDirectory(_folder);

    private static void Shot(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("FIG_SHOT_DIR");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"), 100);
    }

    [AvaloniaFact]
    public void HomeView_ShowsProjectsAndEmptyState()
    {
        var store = new ProjectStore(_folder);
        var vm = new HomeViewModel(store);
        var window = new Window { Width = 1100, Height = 730, Content = new HomeView { DataContext = vm } };
        window.Show();
        Shot(window, "home-empty");

        store.CreateProject("Robot");
        store.CreateProject("Product walkthrough");
        vm.Refresh();
        vm.IsGrid = false;
        Shot(window, "home-list");
        vm.IsGrid = true;
        Shot(window, "home-grid");
        window.Close();
    }

    [AvaloniaFact]
    public void EditorView_LoadsAndSwitchesLayouts()
    {
        var store = new ProjectStore(_folder);
        var project = store.LoadProject(store.CreateProject("Robot"))!;
        var gestures = new GestureRegistry(Path.Combine(_folder, "gestures.json"));
        var vm = new EditorViewModel(gestures);
        vm.LoadProject(project, store);

        var window = new Window { Width = 1440, Height = 960, Content = new EditorView { DataContext = vm } };
        window.Show();
        Shot(window, "editor");

        vm.Layout.ToggleFocusCommand.Execute(null);
        Shot(window, "editor-focus");
        vm.Layout.ResetCommand.Execute(null);
        foreach (var tab in vm.LibraryTabs)
        {
            vm.SelectedLibraryTab = tab;
            Shot(window, "editor-tab-" + tab.Title.ToLowerInvariant());
        }
        window.Close();
        vm.DisposePlayback();
    }

    /// <summary>Opens a project holding two linked video+audio clips, a music clip and a few markers.</summary>
    private EditorViewModel OpenSampleEditor(out Window window)
    {
        var store = new ProjectStore(_folder);
        var project = store.LoadProject(store.CreateProject("Robot"))!;
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Video, Url = "/nonexistent/Robot_01.mp4", DurationSec = 12, Width = 1920, Height = 1080, HasAudio = true, Offline = true });
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Video, Url = "/nonexistent/Robot_02.mp4", DurationSec = 18, Width = 1920, Height = 1080, HasAudio = true, Offline = true });
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Audio, Url = "/nonexistent/Room.wav", DurationSec = 42, HasAudio = true, Offline = true });
        store.SaveProject(project);

        var vm = new EditorViewModel(new GestureRegistry(Path.Combine(_folder, "gestures.json")));
        vm.LoadProject(project, store);
        var rng = new Random(7);
        foreach (var asset in vm.Media)
            asset.WaveformPeaks = Enumerable.Range(0, (int)(asset.DurationSec * 50))
                .Select(i => (float)Math.Clamp(Math.Abs(Math.Sin(i / 7.0)) * (0.35 + 0.65 * rng.NextDouble()), 0, 1)).ToArray();
        vm.Editor.AddMediaNewTracks(vm.Media[0], 0);
        vm.Editor.AddMediaNewTracks(vm.Media[1], 12);
        if (vm.Editor.Document.Tracks.SelectMany(t => t.Clips).FirstOrDefault() is { } firstClip)
            vm.Editor.AddMarker(firstClip, 3, "Beat", "#eab308");
        vm.Editor.Document.Markers.Add(new Fig.Core.Timeline.Marker { Name = "Beat", StartSec = 15, Color = "#EAB308" });
        // no audio device in tests: drop the playback engine so it can't reset the playhead behind our back
        vm.DisposePlayback();
        window = new Window { Width = 1440, Height = 970, Content = new EditorView { DataContext = vm } };
        window.Show();
        return vm;
    }

    [AvaloniaFact]
    public void EditorView_MarkersWorkflow_ShowsPanelTimelineAndInspector()
    {
        var vm = OpenSampleEditor(out var window);
        var editor = vm.Editor;
        editor.Document.Markers.Clear();
        editor.AddRangeMarker(0, 4, "Intro", "#5f9cf8");
        var save = editor.AddRangeMarker(4, 8, "Save changes", "#5ba3b0");
        var zoom = editor.AddMarker(editor.Document, 5, "Zoom finishes", "#eab308");

        vm.SelectedLibraryTab = vm.LibraryTabs.First(t => t.Title == "Markers");
        Shot(window, "markers-panel");
        Assert.Equal(3, vm.Markers.SequenceMarkers.Count);

        vm.SelectMarker(save.Id);
        Shot(window, "markers-range-selected");
        Assert.Equal("Range marker", vm.Properties.Title);

        vm.SelectMarker(zoom.Id);
        Shot(window, "markers-point-selected");
        Assert.Equal("Point marker", vm.Properties.Title);

        var dialog = new BookmarkRangesDialog(editor.Document);
        dialog.Show();
        Shot(dialog, "markers-bookmark-dialog");
        Assert.True(dialog.Model.CanCreate);
        dialog.Close();

        window.Close();
        vm.DisposePlayback();
    }

    /// <summary>Where a point inside the picture (0..1 from its top-left) lands in window coordinates.</summary>
    private static Point PicturePoint(Control overlay, Window window, double aspect, double nx, double ny)
    {
        var w = overlay.Bounds.Width;
        var h = w / aspect;
        if (h > overlay.Bounds.Height)
        {
            h = overlay.Bounds.Height;
            w = h * aspect;
        }
        var local = new Point((overlay.Bounds.Width - w) / 2 + nx * w, (overlay.Bounds.Height - h) / 2 + ny * h);
        var origin = overlay.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("overlay is not in the window");
        return new Point(origin.X + local.X, origin.Y + local.Y);
    }

    private static (Fig.Core.Timeline.Clip Clip, Fig.Core.Timeline.EffectInstance Fx) AddMotion(EditorViewModel vm, string preset = "zoom-in-out", double at = 2)
    {
        var video = vm.Editor.Document.Tracks.SelectMany(t => t.Clips).First(c => c is Fig.Core.Timeline.VideoClip);
        var fx = Fig.Core.Media.EffectPresetInstantiator.Create(Fig.Core.Media.BuiltInEffectPresets.Find(preset)!, video.DurSec, at);
        vm.Editor.AddEffect(video.Id, fx);
        vm.Properties.SelectEffect(video.Id, fx.Id);
        return (video, fx);
    }

    [AvaloniaFact]
    public void EditorView_Transform_StopwatchTurnsAnimationOnAndOff()
    {
        var vm = OpenSampleEditor(out var window);
        var video = vm.Editor.Document.Tracks.SelectMany(t => t.Clips).First(c => c is Fig.Core.Timeline.VideoClip);
        var fx = Fig.Core.Media.EffectCatalog.Find("transform")!.CreateInstance();
        vm.Editor.AddEffect(video.Id, fx);
        vm.Properties.SelectEffect(video.Id, fx.Id);
        var t = vm.Properties.Transform;

        Assert.False(t.IsAnimated);
        t.SetCamera(0.4, 0.4, 2);                                   // not animated: one look for the whole clip
        Assert.Empty(fx.Keyframes);
        Assert.Equal(2, fx.Params["zoom"].AsDouble, 3);

        vm.SeekFromUser(video.StartSec + 4);
        t.ToggleAnimateCommand.Execute(null);                        // stopwatch on: one keyframe holding that look
        Assert.True(t.IsAnimated);
        Assert.Equal(new[] { 4.0 }, fx.Keyframes["zoom"].Select(k => k.TimeSec).ToArray());

        vm.SeekFromUser(video.StartSec + 8);
        t.SetCamera(0.4, 0.4, 3);                                   // animated: this becomes a second keyframe
        Assert.Equal(2, fx.Keyframes["zoom"].Count);

        t.ToggleAnimateCommand.Execute(null);                        // stopwatch off: back to one look, as it is here
        Assert.False(t.IsAnimated);
        Assert.Empty(fx.Keyframes);
        Assert.Equal(3, fx.Params["zoom"].AsDouble, 3);

        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void EditorView_KeyframeRuler_SeeksAddsAndRetimesKeyframesWithTheMouse()
    {
        var vm = OpenSampleEditor(out var window);
        var (clip, fx) = AddMotion(vm);                              // 4 keyframes: 2.0, 3.2, 4.8, 6.0
        window.UpdateLayout();
        var ruler = window.GetVisualDescendants().OfType<Fig.App.Controls.KeyframeRuler>().First();
        Point ToWindow(Point p) => ruler.TranslatePoint(p, window)!.Value;

        // click an empty spot: the playhead moves there
        var empty = ToWindow(ruler.PointForTime(9));
        window.MouseDown(empty, Avalonia.Input.MouseButton.Left);
        window.MouseUp(empty, Avalonia.Input.MouseButton.Left);
        Assert.InRange(vm.PlayheadTimeSec - clip.StartSec, 8.7, 9.3);

        // click a diamond: selects it and jumps to it
        var key = fx.Keyframes["zoom"][1].TimeSec;
        var diamond = ToWindow(ruler.PointForTime(key));
        window.MouseDown(diamond, Avalonia.Input.MouseButton.Left);
        window.MouseUp(diamond, Avalonia.Input.MouseButton.Left);
        Assert.Equal(key, vm.PlayheadTimeSec - clip.StartSec, 1);
        Assert.Equal(key, vm.Properties.SelectedKeyframeSec!.Value, 3);

        // drag it later
        var target = ToWindow(ruler.PointForTime(key + 0.8));
        window.MouseDown(diamond, Avalonia.Input.MouseButton.Left);
        window.MouseMove(target);
        window.MouseUp(target, Avalonia.Input.MouseButton.Left);
        Assert.DoesNotContain(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - key) < 1e-6);
        Assert.Contains(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - (key + 0.8)) < 0.1);

        // double-click empty space: adds a keyframe there
        var before = fx.Keyframes["zoom"].Count;
        var spot = ToWindow(ruler.PointForTime(12));
        window.MouseDown(spot, Avalonia.Input.MouseButton.Left);
        window.MouseUp(spot, Avalonia.Input.MouseButton.Left);
        window.MouseDown(spot, Avalonia.Input.MouseButton.Left);
        window.MouseUp(spot, Avalonia.Input.MouseButton.Left);
        Assert.Equal(before + 1, fx.Keyframes["zoom"].Count);

        Shot(window, "keyframe-ruler");
        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void EditorView_Transform_DraggingTheFrameKeysItAtThePlayhead()
    {
        var vm = OpenSampleEditor(out var window);
        var (clip, fx) = AddMotion(vm);
        var keysBefore = fx.Keyframes["zoom"].Count;

        // park the playhead between keyframes (not on one) and drag the frame
        var between = (fx.Keyframes["zoom"][1].TimeSec + fx.Keyframes["zoom"][2].TimeSec) / 2 + 0.1;
        vm.SeekFromUser(clip.StartSec + between);
        Assert.False(vm.Properties.Transform.IsOnKeyframe);

        window.UpdateLayout();
        var overlay = window.GetVisualDescendants().OfType<Fig.App.Controls.TransformOverlay>().First();
        var aspect = vm.Properties.Transform.FrameAspect;
        var cx = vm.Properties.Transform.CenterX;
        var cy = vm.Properties.Transform.CenterY;
        var from = PicturePoint(overlay, window, aspect, cx, cy);
        var to = PicturePoint(overlay, window, aspect, cx + 0.1, cy + 0.05);
        window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, Avalonia.Input.MouseButton.Left);

        Assert.Equal(keysBefore + 1, fx.Keyframes["zoom"].Count);                   // a keyframe appeared at the playhead
        Assert.Contains(fx.Keyframes["centerX"], k => Math.Abs(k.TimeSec - between) < 0.01 && k.Value.AsDouble > cx + 0.05);
        Assert.True(vm.Properties.Transform.IsOnKeyframe);

        // and it undoes as one step
        vm.Editor.Undo();
        Assert.Equal(keysBefore, fx.Keyframes["zoom"].Count);

        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void EditorView_EffectBar_KeyframesCanBeAddedAndDraggedOnTheTimeline()
    {
        var vm = OpenSampleEditor(out var window);
        var (clip, fx) = AddMotion(vm);
        window.UpdateLayout();
        var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
        Point ToWindow(Point p) => timeline.TranslatePoint(p, window)!.Value;

        // double-click empty key lane to add a keyframe
        var lane = timeline.RackPointFor(clip.Id, fx.Id)!.Value;
        var spot = ToWindow(new Point(lane.X + 20, lane.Y));
        var before = fx.Keyframes["zoom"].Count;
        window.MouseDown(spot, Avalonia.Input.MouseButton.Left);
        window.MouseUp(spot, Avalonia.Input.MouseButton.Left);
        window.MouseDown(spot, Avalonia.Input.MouseButton.Left);
        window.MouseUp(spot, Avalonia.Input.MouseButton.Left);
        Assert.Equal(before + 1, fx.Keyframes["zoom"].Count);

        // drag the first keyframe's diamond 0.5s later
        var first = fx.Keyframes["zoom"][0].TimeSec;
        var diamond = ToWindow(timeline.RackPointFor(clip.Id, fx.Id, first)!.Value);
        var target = new Point(diamond.X + 50, diamond.Y);
        window.MouseDown(diamond, Avalonia.Input.MouseButton.Left);
        window.MouseMove(target);
        window.MouseUp(target, Avalonia.Input.MouseButton.Left);
        Assert.True(fx.Keyframes["zoom"].Any(k => Math.Abs(k.TimeSec - (first + 0.5)) < 0.05), "the keyframe should have moved about half a second later");
        Assert.DoesNotContain(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - first) < 1e-6);

        Shot(window, "transform-bar-keyframes");
        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void EditorView_SwirlTransition_ShowsInspectorAndHandles()
    {
        var vm = OpenSampleEditor(out var window);
        var editor = vm.Editor;
        var v1 = editor.Document.Tracks.First(t => t.Kind == Fig.Core.Timeline.TrackKind.Video);
        var left = v1.Clips.First();
        var right = editor.AddMediaLinked(vm.Media[1], v1.Id, left.StartSec + left.DurSec)!;
        Assert.True(editor.ApplyTransitionAtCut(left.Id, right.Id, Fig.Core.Media.TransitionCatalog.Find("swirl")!.CreateRef()));
        editor.Selection.SelectTransition($"{left.Id}|{right.Id}");

        Assert.True(vm.Properties.Swirl.IsActive);
        Assert.Equal("Transition", vm.Properties.Title);
        Shot(window, "swirl-inspector");

        vm.Properties.Swirl.Set(centerX: 0.3, radius: 0.6, rotation: 180);
        Assert.Equal(0.3, vm.Properties.Swirl.CenterX, 3);
        Assert.Equal(180, vm.Properties.Swirl.RotationDegrees, 3);

        vm.Properties.ResetTransitionControlsCommand.Execute(null);
        Assert.Equal(0.5, vm.Properties.Swirl.CenterX, 3);
        Assert.Equal(90, vm.Properties.Swirl.RotationDegrees, 3);

        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void Icons_LoadEveryShapeRegardlessOfAttributeOrder()
    {
        // timer.svg writes its <line> attributes as x1, x2, y1, y2 and has a circle: all three shapes must load
        Assert.Equal(3, Fig.App.Services.IconService.Get("timer").Count);
        // eye is a path plus a circle
        Assert.Equal(2, Fig.App.Services.IconService.Get("eye").Count);
        // a few that mix lines, rects and paths
        foreach (var key in new[] { "scissors", "link", "clapperboard", "film", "flag", "diamond", "chevron-right", "trash-2" })
            Assert.NotEmpty(Fig.App.Services.IconService.Get(key));
    }

    [AvaloniaFact]
    public void ExportDialog_BuildsInBothModes()
    {
        var timeline = new Fig.Core.Timeline.Timeline { Rate = new Fig.Core.Timeline.FrameRate(30, 1) };
        timeline.Tracks.Add(new Fig.Core.Timeline.Track
        {
            Kind = Fig.Core.Timeline.TrackKind.Video,
            Clips = { new Fig.Core.Timeline.VideoClip { DurSec = 12 } },
        });
        timeline.Markers.Add(new Fig.Core.Timeline.Marker { Name = "Intro", StartSec = 0, DurSec = 4, Color = "#5F9CF8" });
        timeline.Markers.Add(new Fig.Core.Timeline.Marker { Name = "Save changes", StartSec = 4, DurSec = 8, Color = "#5BA3B0" });

        var single = new ExportDialog(30, 1920, 1080, timeline, "Robot", chapterMode: false);
        single.Show();
        Shot(single, "export-single");
        single.Close();

        var ranges = new ExportDialog(30, 1920, 1080, timeline, "Robot", chapterMode: true);
        ranges.Show();
        Assert.Equal(2, ranges.Model.Chapters.Count);
        Shot(ranges, "export-ranges");
        ranges.Close();
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { /* temp cleanup is best-effort */ }
    }
}
