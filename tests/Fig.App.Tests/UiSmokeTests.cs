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
        DataTemplates.Add(new Fig.App.ViewLocator());
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        foreach (var sheet in new[] { "FigTheme", "FigControls" })
            Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Fig.App/"))
            {
                Source = new Uri($"avares://Fig.App/Styles/{sheet}.axaml"),
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

        vm.Layout.ToggleLeftCommand.Execute(null);
        vm.Layout.ToggleRightCommand.Execute(null);
        Shot(window, "editor-panels-hidden");
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
    private EditorViewModel OpenSampleEditor(out Window window) => OpenSampleEditor(out window, inShell: false);

    /// <summary>The whole app window (custom title bar and menus) around the same sample project.</summary>
    private AppViewModel OpenSampleShell(out MainWindow window)
    {
        var store = new ProjectStore(_folder);
        var app = new AppViewModel(store, new GestureRegistry(Path.Combine(_folder, "gestures.json")));
        OpenSampleEditor(out _, inShell: true, existing: app.Editor, store: store);
        app.CurrentView = app.Editor;
        window = new MainWindow { Width = 1440, Height = 970, DataContext = app };
        window.Show();
        return app;
    }

    private EditorViewModel OpenSampleEditor(out Window window, bool inShell, EditorViewModel? existing = null, ProjectStore? store = null)
    {
        store ??= new ProjectStore(_folder);
        var project = store.LoadProject(store.CreateProject("Robot"))!;
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Video, Url = "/nonexistent/Robot_01.mp4", DurationSec = 12, Width = 1920, Height = 1080, HasAudio = true, Offline = true });
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Video, Url = "/nonexistent/Robot_02.mp4", DurationSec = 18, Width = 1920, Height = 1080, HasAudio = true, Offline = true });
        project.Media.Add(new Fig.Core.Media.MediaAsset { Kind = Fig.Core.Media.MediaKind.Audio, Url = "/nonexistent/Room.wav", DurationSec = 42, HasAudio = true, Offline = true });
        store.SaveProject(project);

        var vm = existing ?? new EditorViewModel(new GestureRegistry(Path.Combine(_folder, "gestures.json")));
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
        if (inShell)
        {
            window = null!;
            return vm;
        }
        window = new Window { Width = 1440, Height = 970, Content = new EditorView { DataContext = vm } };
        window.Show();
        return vm;
    }

    [AvaloniaFact]
    public void Inspector_ShowsOneTabPerFacetTheSelectionShares()
    {
        var app = OpenSampleShell(out var window);
        var vm = app.Editor;
        try
        {
            var clips = vm.Editor.Document.Tracks.SelectMany(t => t.Clips).ToList();
            var video = clips.OfType<Fig.Core.Timeline.VideoClip>().First();
            // the second video's sound, detached to an audio clip of its own
            var audio = vm.Editor.DetachAudio([clips.OfType<Fig.Core.Timeline.VideoClip>().Last().Id]).Single();
            string[] Tabs() => vm.Properties.Sections.Select(s => s.Title).ToArray();

            Assert.Empty(vm.Properties.Sections);

            vm.Editor.Selection.SelectOnly(video.Id);
            window.UpdateLayout();
            Assert.Equal(["Picture", "Audio", "Speed", "Fades", "Effects", "File"], Tabs());
            Assert.Equal("Video clip", vm.Properties.Title);
            Shot(window, "inspector-video");

            vm.Editor.Selection.SelectOnly(audio.Id);
            Assert.Equal(["Audio", "Speed", "Fades", "Effects", "File"], Tabs());
            Assert.Equal("Audio clip", vm.Properties.Title);

            // a video and an audio clip together: only what both have
            vm.Editor.Selection.SelectClips([video.Id, audio.Id]);
            Assert.Equal(["Audio", "Speed", "Fades", "Effects", "File"], Tabs());
            Assert.Equal("2 selected", vm.Properties.Title);

            var marker = vm.Editor.Document.Markers.First();
            vm.Editor.Selection.SelectMarker(marker.Id);
            Assert.Equal(["Marker"], Tabs());
            Assert.False(vm.Properties.ShowTabStrip);

            var fx = Fig.Core.Media.EffectCatalog.Find(Fig.Core.Media.EffectCatalog.Sepia)!.CreateInstance();
            vm.Editor.AddEffect(video.Id, fx);
            vm.Properties.SelectEffect(video.Id, fx.Id);
            Assert.Equal(["Effects"], Tabs());
            Assert.Equal("Sepia", vm.Properties.Title);
            Shot(window, "inspector-effect");

            var track = vm.Editor.Document.Tracks[0];
            vm.Editor.Selection.ActiveTrackId = track.Id;
            Assert.Equal(["Effects", "Track"], Tabs());

            // library media is inspected the same way
            vm.Editor.Selection.Clear();
            vm.SelectedMedia = vm.Media[0];
            Assert.Equal(["File"], Tabs());
            Assert.Equal("Media", vm.Properties.Title);

            // a rack row can open its own tab
            vm.SelectedMedia = null;
            vm.Editor.Selection.SelectOnly(audio.Id);
            vm.Properties.OpenSection(Fig.Core.Timeline.Facet.Fades);
            Assert.Equal("Fades", vm.Properties.SelectedSection?.Title);
        }
        finally
        {
            window.Close();
            vm.DisposePlayback();
        }
    }

    [AvaloniaFact]
    public void MainWindow_DrawsItsOwnTitleBarWithTheMenusInsideIt()
    {
        var app = OpenSampleShell(out var window);
        try
        {
            window.UpdateLayout();
            Shot(window, "shell-editor");
            var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
            // the menus are a standard Menu, in the title bar row rather than a bar of their own beneath it
            var menu = bar.GetVisualDescendants().OfType<Menu>().Single();
            Assert.Equal(new object[] { "_File", "_Edit", "_View", "_Help" }, menu.Items.OfType<MenuItem>().Select(m => m.Header).ToArray());
            Assert.True(bar.Bounds.Height < 60);
            Assert.True(app.IsEditing);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);

            var edit = menu.Items.OfType<MenuItem>().First(m => Equals(m.Header, "_Edit"));
            edit.Open();
            window.UpdateLayout();
            Shot(window, "shell-menu-edit");
            Assert.True(edit.IsSubMenuOpen);
            // shortcut text comes from the shortcut table, so a rebind shows in the menu
            var undo = edit.Items.OfType<MenuItem>().First(m => Equals(m.Header, "_Undo"));
            Assert.Equal(Avalonia.Input.KeyGesture.Parse("Ctrl+Z"), undo.InputGesture);
            edit.Close();
        }
        finally
        {
            window.Close();
            app.Editor.DisposePlayback();
        }
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

    [AvaloniaFact]
    public void MarkersPanel_SplitVideoButton_CutsAtSelectedMarkers_AndUndoesTogether()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var markers = new[] { vm.Editor.AddMarker(vm.Editor.Document, 2), vm.Editor.AddMarker(vm.Editor.Document, 4) };
            vm.Editor.Selection.SelectMarkers(markers.Select(marker => marker.Id));
            vm.SelectedLibraryTab = vm.LibraryTabs.First(tab => tab.Tab == LibraryPanelTab.Markers);
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(control => Equals(control.Content, "Split video at markers"));
            Assert.True(button.IsEnabled);
            int videosBefore = vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.VideoClip>().Count();
            int audioBefore = vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.AudioClip>().Count();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Assert.Equal(videosBefore + 2, vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.VideoClip>().Count());
            // the sound is part of the video clip, so it is cut with it
            Assert.Equal(audioBefore, vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.AudioClip>().Count());
            vm.Editor.Undo();
            Assert.Equal(videosBefore, vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.VideoClip>().Count());
            Assert.Equal(audioBefore, vm.Editor.Document.Tracks.SelectMany(track => track.Clips).OfType<Fig.Core.Timeline.AudioClip>().Count());
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void EditorSpace_DoesNotClickFocusedDeleteButton()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var marker = vm.Editor.AddMarker(vm.Editor.Document, 2);
            vm.Editor.Selection.SelectMarker(marker.Id);
            vm.SelectedLibraryTab = vm.LibraryTabs.First(tab => tab.Tab == LibraryPanelTab.Markers);
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(control => Equals(control.Content, "Delete selected"));
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.NotNull(vm.Editor.FindMarker(marker.Id));
            Assert.NotNull(vm.TogglePlaybackCommand.ExecutionTask);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void CreativeLibrary_CategoriesSearchAndActualThumbnailsLoad()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            vm.SelectedLibraryTab = vm.LibraryTabs.First(t => t.Tab == LibraryPanelTab.Effects);
            vm.SelectedEffectCategory = "Light";
            Assert.Contains(vm.FilteredEffects, e => e.TypeId == "glow");
            Assert.All(vm.FilteredEffects, e => Assert.Equal("Light", e.Category));
            Assert.Contains(vm.FilteredPresets, p => p.Id == "glow-hit");
            Assert.Contains(vm.FilteredPresets, p => p.Id == "white-flash-hit");
            window.UpdateLayout();
            Shot(window, "creative-light-effects");
            Assert.NotEmpty(window.GetVisualDescendants().OfType<EffectPreview>());

            vm.SelectedEffectCategory = "Motion";
            Assert.Contains(vm.FilteredEffects, e => e.TypeId == "camera-shake");
            vm.SelectedEffectCategory = "All";
            vm.EffectSearchText = "glitch";
            Assert.Contains(vm.FilteredEffects, e => e.TypeId == "digital-glitch");
            Assert.Contains(vm.FilteredPresets, p => p.Id == "glitch-burst");

            vm.SelectedLibraryTab = vm.LibraryTabs.First(t => t.Tab == LibraryPanelTab.Transitions);
            vm.SelectedTransitionCategory = "Camera";
            vm.TransitionSearchText = "Zoom";
            Assert.Contains(vm.FilteredTransitions, t => t.TypeId == "zoom-in");
            Assert.Contains(vm.FilteredTransitions, t => t.TypeId == "zoom-out");
            Assert.All(vm.FilteredTransitions, t => Assert.Equal("Camera", t.Category));
            vm.TransitionSearchText = "";
            window.UpdateLayout();
            Shot(window, "creative-camera-transitions");
            Assert.NotEmpty(window.GetVisualDescendants().OfType<TransitionPreview>());
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void GraphEditor_BuildSaveAndUseAnEffect()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            string? toast = null;
            vm.Notify = message => toast = message;
            vm.NewGraphEffectCommand.Execute(null);
            window.UpdateLayout();
            Assert.True(vm.IsGraphEditorOpen);
            var editor = vm.GraphEditor!;
            Assert.True(editor.IsValid);

            // splice a blur into the starter Input → Output wire and pin its radius
            var blur = editor.InsertOnEdge(editor.Graph.Edges.Single(), "blur")!;
            editor.TogglePromote(blur.Id, "radius");
            editor.Name = "Soft focus";
            window.UpdateLayout();
            Assert.True(editor.IsValid);
            Assert.True(editor.IsDirty);
            Assert.Single(editor.Exposed);
            Shot(window, "graph-editor");

            editor.SaveCommand.Execute(null);
            Assert.False(editor.IsDirty);
            var entry = Assert.Single(Fig.Core.Media.EffectCatalog.All, e => e.IsCustom);
            Assert.Equal("Soft focus", entry.DisplayName);
            Assert.Equal("radius", Assert.Single(entry.ParamSchema).Key);

            // it shows up under My effects, like any other effect
            vm.SelectedEffectCategory = "My effects";
            Assert.Contains(vm.FilteredEffects, e => e.TypeId == entry.TypeId);
            Assert.Equal(1, vm.Project!.EffectGraphs.Count);

            // closing a saved graph does not prompt
            editor.BackCommand.Execute(null);
            Assert.False(vm.IsGraphEditorOpen);
            Assert.Null(toast is null ? null : (string?)null);
        }
        finally
        {
            vm.EffectGraphs.Detach();
            window.Close();
            vm.DisposePlayback();
        }
    }

    [AvaloniaFact]
    public void GraphEditor_WiresAreMadeByDraggingBetweenPorts_AndRefusedWhenTheyCannotConnect()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            vm.NewGraphEffectCommand.Execute(null);
            var editor = vm.GraphEditor!;
            // start from nothing wired: remove the starter wire, add a blur and a noise mask
            editor.Disconnect(editor.Graph.Edges.Single());
            var blur = editor.AddNode("blur", 260, 40);
            var noise = editor.AddNode("noise", 260, 220);
            window.UpdateLayout();
            var canvas = window.GetVisualDescendants().OfType<GraphCanvas>().First();
            Avalonia.Point W(Avalonia.Point p) => canvas.TranslatePoint(p, window)!.Value;

            void Drag(Avalonia.Point from, Avalonia.Point to)
            {
                window.MouseDown(from, Avalonia.Input.MouseButton.Left);
                window.MouseMove(new Avalonia.Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
                window.MouseMove(to);
                window.MouseUp(to, Avalonia.Input.MouseButton.Left);
            }

            // Input.image → blur.image connects
            Drag(W(canvas.PortPoint("input", "image", true)!.Value), W(canvas.PortPoint(blur.Id, "image", false)!.Value));
            Assert.Contains(editor.Graph.Edges, e => e.FromNodeId == "input" && e.ToNodeId == blur.Id);

            // a mask cannot go into an image input: refused, nothing changes
            var before = editor.Graph.Edges.Count;
            var refused = false;
            canvas.Refused += _ => refused = true;
            Drag(W(canvas.PortPoint(noise.Id, "mask", true)!.Value), W(canvas.PortPoint("output", "image", false)!.Value));
            Assert.True(refused);
            Assert.Equal(before, editor.Graph.Edges.Count);

            // blur.image → output.image, then the graph is valid and nodes can be moved by dragging a header
            Drag(W(canvas.PortPoint(blur.Id, "image", true)!.Value), W(canvas.PortPoint("output", "image", false)!.Value));
            Assert.True(editor.IsValid);
            var startX = blur.X;
            var header = W(canvas.NodeHeaderPoint(blur.Id)!.Value);
            Drag(header, new Avalonia.Point(header.X + 60, header.Y + 30));
            Assert.True(blur.X > startX + 20);

            // clicking a pin exposes the value; clicking again hides it
            var pin = W(canvas.PinPoint(blur.Id, "radius")!.Value);
            window.MouseDown(pin, Avalonia.Input.MouseButton.Left);
            window.MouseUp(pin, Avalonia.Input.MouseButton.Left);
            Assert.True(editor.IsPromoted(blur.Id, "radius"));
            editor.UndoCommand.Execute(null);
            Assert.False(editor.IsPromoted(blur.Id, "radius"));
            Shot(window, "graph-editor-wired");
        }
        finally
        {
            vm.EffectGraphs.Detach();
            window.Close();
            vm.DisposePlayback();
        }
    }

    [AvaloniaFact]
    public void PlainLetterShortcuts_NeverSwallowTypingInATextBox()
    {
        var app = new AppViewModel(new ProjectStore(_folder), new GestureRegistry(Path.Combine(_folder, "gestures.json")));
        var window = new MainWindow { DataContext = app };
        var box = new TextBox();
        var other = new Button();
        var panel = new StackPanel { Children = { box, other } };
        window.Content = panel;
        window.Show();
        try
        {
            var handled = new List<bool>();
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

            // J, K, L and Shift+S are transport shortcuts, but typing them in a text box must leave them unhandled
            box.Focus();
            foreach (var (key, mods) in new[] { (Key.K, RawInputModifiers.None), (Key.L, RawInputModifiers.None),
                                                 (Key.J, RawInputModifiers.None), (Key.S, RawInputModifiers.Shift) })
            {
                window.KeyPress(key, mods, Enum.Parse<PhysicalKey>(key.ToString()), key.ToString().ToLowerInvariant());
                window.KeyRelease(key, mods, Enum.Parse<PhysicalKey>(key.ToString()), null);
            }
            Assert.Equal(4, handled.Count);
            Assert.All(handled, h => Assert.False(h));

            // elsewhere they still drive the transport
            handled.Clear();
            other.Focus();
            window.KeyPress(Key.K, RawInputModifiers.None, PhysicalKey.K, "k");
            Assert.Single(handled);
            Assert.True(handled[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Timeline_RulerStaysPinnedWhileTheTracksScrollUnderIt()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            for (var i = 0; i < 8; i++)
                vm.Editor.AddTrack(Fig.Core.Timeline.TrackKind.Video);
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
            var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "TimelineScroller");
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the tracks should overflow the pane");

            scroller.Offset = new Avalonia.Vector(0, 120);
            window.UpdateLayout();
            Assert.True(scroller.Offset.Y > 60);
            Shot(window, "timeline-pinned-ruler");

            // a click at the very top of the pane is still on the ruler: it moves the playhead, it does not hit a track
            vm.PlayheadTimeSec = 0;
            var x = TimelineView.TrackHeaderWidth + timeline.Viewport.TimeToX(6);
            var point = scroller.TranslatePoint(new Avalonia.Point(x, 10), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Assert.InRange(vm.PlayheadTimeSec, 5.5, 6.5);
            Assert.Empty(vm.Editor.Selection.SelectedClipIds);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void Timeline_EveryVisibleEffectBarCanBeSelected_OnACutUpTrack()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var track = vm.Editor.Document.Tracks.First(t => t.Kind == Fig.Core.Timeline.TrackKind.Video);
            // cut the picture up the way beat cutting does
            foreach (var at in Enumerable.Range(1, 22).Select(i => i * 0.5 + 0.13))
            {
                var clip = track.Clips.FirstOrDefault(c => c.StartSec < at && at < c.StartSec + c.DurSec);
                if (clip is not null) vm.Editor.Cut(clip.Id, at);
            }
            var presets = Fig.Core.Media.BuiltInEffectPresets.All.ToArray();
            var effects = new List<Fig.Core.Timeline.EffectInstance>();
            for (var i = 0; i < 14; i++)
            {
                var preset = presets[i % presets.Length];
                var start = 0.4 + i * 0.55;
                var fx = Fig.Core.Media.EffectPresetInstantiator.Create(preset, preset.DefaultDurationSec + start, start);
                vm.Editor.AddEffect(track.Id, fx);
                effects.Add(fx);
            }
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();

            var failures = new List<string>();
            var tried = 0;
            foreach (var fx in effects)
            {
                var lane = timeline.RackPointFor(track.Id, fx.Id)!.Value;
                var p = timeline.TranslatePoint(new Avalonia.Point(lane.X, lane.Y - 12), window)!.Value;
                if (p.X < 0 || p.X > window.Width || p.Y < 0 || p.Y > window.Height - 40)
                    continue; // scrolled out of the pane
                tried++;
                // start from the state in which it was reported: every clip selected and the track active
                vm.Editor.Selection.SelectClips(track.Clips.Select(c => c.Id));
                vm.Editor.Selection.ActiveTrackId = track.Id;
                vm.Properties.Refresh();
                window.MouseMove(p);
                window.MouseDown(p, Avalonia.Input.MouseButton.Left);
                // a real hand wobbles by a pixel or two between press and release
                window.MouseMove(new Avalonia.Point(p.X + 1, p.Y));
                window.MouseMove(new Avalonia.Point(p.X + 2, p.Y + 1));
                window.MouseUp(new Avalonia.Point(p.X + 2, p.Y + 1), Avalonia.Input.MouseButton.Left);
                if (vm.Properties.SelectedEffect?.EffectId != fx.Id || vm.Editor.Selection.SelectedEffect?.EffectId != fx.Id)
                    failures.Add($"{fx.TypeId} {fx.StartSec:0.00}+{fx.DurSec:0.00} at ({p.X:0},{p.Y:0}): got {vm.Editor.Selection.SelectedEffect?.EffectId?[..6] ?? "none"}; clips={vm.Editor.Selection.SelectedClipIds.Count}");
            }
            Assert.True(tried >= 6, $"only {tried} bars were on screen");
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void Timeline_EffectBars_MultiSelect_WithModifiersAndABox_ThenMoveAndDeleteTogether()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var track = vm.Editor.Document.Tracks.First(t => t.Kind == Fig.Core.Timeline.TrackKind.Video);
            var bars = new List<Fig.Core.Timeline.EffectInstance>();
            foreach (var (start, dur) in new[] { (1.0, 0.8), (2.2, 0.8), (3.4, 0.8), (6.0, 0.8) })
            {
                var fx = Fig.Core.Media.EffectCatalog.Find("camera-shake")!.CreateInstance();
                fx.StartSec = start;
                fx.DurSec = dur;
                vm.Editor.AddEffect(track.Id, fx);
                bars.Add(fx);
            }
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
            var selection = vm.Editor.Selection;

            Avalonia.Point Label(Fig.Core.Timeline.EffectInstance fx)
            {
                var lane = timeline.RackPointFor(track.Id, fx.Id)!.Value;
                return timeline.TranslatePoint(new Avalonia.Point(lane.X, lane.Y - 12), window)!.Value;
            }
            void Click(Avalonia.Point p, Avalonia.Input.RawInputModifiers mods = Avalonia.Input.RawInputModifiers.None)
            {
                window.MouseDown(p, Avalonia.Input.MouseButton.Left, mods);
                window.MouseUp(p, Avalonia.Input.MouseButton.Left, mods);
            }

            // a plain click selects one; Shift-click adds, and Shift-click again takes a bar out
            Click(Label(bars[0]));
            Assert.Equal(new[] { bars[0].Id }, selection.SelectedEffects.Select(e => e.EffectId));
            Click(Label(bars[1]), Avalonia.Input.RawInputModifiers.Shift);
            Click(Label(bars[2]), Avalonia.Input.RawInputModifiers.Shift);
            Assert.Equal(3, selection.SelectedEffects.Count);
            Click(Label(bars[1]), Avalonia.Input.RawInputModifiers.Shift);
            Assert.Equal(new[] { bars[0].Id, bars[2].Id }, selection.SelectedEffects.Select(e => e.EffectId));
            Assert.Empty(selection.SelectedClipIds);

            // Ctrl-click toggles too
            Click(Label(bars[3]), Avalonia.Input.RawInputModifiers.Control);
            Assert.Equal(3, selection.SelectedEffects.Count);
            Click(Label(bars[3]), Avalonia.Input.RawInputModifiers.Control);
            Assert.Equal(2, selection.SelectedEffects.Count);

            // a box dragged from empty space in the effect strip picks the bars it touches (and no clips)
            selection.Clear();
            var first = Label(bars[0]);
            var third = Label(bars[2]);
            var from = new Avalonia.Point(first.X - 70, first.Y);
            var to = new Avalonia.Point(third.X + 15, first.Y + 12);
            window.MouseDown(from, Avalonia.Input.MouseButton.Left);
            window.MouseMove(new Avalonia.Point((from.X + to.X) / 2, to.Y));
            window.MouseMove(to);
            window.MouseUp(to, Avalonia.Input.MouseButton.Left);
            Assert.Equal(new[] { bars[0].Id, bars[1].Id, bars[2].Id }, selection.SelectedEffects.Select(e => e.EffectId).OrderBy(i => bars.FindIndex(b => b.Id == i)));
            Assert.Empty(selection.SelectedClipIds);

            // dragging one of them moves all three by the same amount, in one undo step
            var starts = bars.Take(3).Select(b => b.StartSec!.Value).ToArray();
            var grab = Label(bars[1]);
            window.MouseDown(grab, Avalonia.Input.MouseButton.Left);
            window.MouseMove(new Avalonia.Point(grab.X + 20, grab.Y));
            window.MouseMove(new Avalonia.Point(grab.X + 60, grab.Y));
            window.MouseUp(new Avalonia.Point(grab.X + 60, grab.Y), Avalonia.Input.MouseButton.Left);
            var shift = bars[0].StartSec!.Value - starts[0];
            Assert.True(shift > 0.2, $"the group should have moved, moved {shift}");
            Assert.Equal(starts[1] + shift, bars[1].StartSec!.Value, 3);
            Assert.Equal(starts[2] + shift, bars[2].StartSec!.Value, 3);
            Assert.Equal(3, selection.SelectedEffects.Count);
            Assert.Equal(6.0, bars[3].StartSec!.Value, 3);   // the unselected bar stayed
            vm.Editor.Undo();
            Assert.Equal(starts, bars.Take(3).Select(b => b.StartSec!.Value).ToArray());

            // Delete removes every selected bar at once
            Click(Label(bars[0]));
            Click(Label(bars[1]), Avalonia.Input.RawInputModifiers.Shift);
            window.KeyPress(Avalonia.Input.Key.Delete, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Delete, null);
            window.KeyRelease(Avalonia.Input.Key.Delete, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Delete, null);
            Assert.Equal(new[] { bars[2].Id, bars[3].Id }, track.Effects.Select(e => e.Id));
            vm.Editor.Undo();
            Assert.Equal(4, track.Effects.Count);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void Beats_WithoutAnAudioClipSelected_ExplainsAndLeavesTheTimelineAlone()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            string? toast = null;
            vm.Notify = message => toast = message;
            vm.Editor.Selection.Clear();
            int changes = 0;
            vm.Editor.TimelineChanged += () => changes++;

            vm.Beats.AnalyzeCommand.Execute(null);

            Assert.False(vm.Beats.HasPreview);
            Assert.Equal("Select a clip with sound first.", vm.Beats.Status);
            Assert.Equal(vm.Beats.Status, toast);
            Assert.Equal("Beats", vm.Beats.ButtonLabel);
            Assert.Null(vm.Editor.BeatPreview);
            Assert.Equal(0, changes);
            vm.Beats.CancelCommand.Execute(null);
            Assert.Equal("", vm.Beats.Status);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void MarkerPanel_ModifierSelection_DeleteAndUndo_WorkWithMouseAndKeyboard()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            vm.Editor.Document.Markers.Clear();
            foreach (var clip in vm.Editor.Document.Tracks.SelectMany(track => track.Clips))
                vm.Editor.ClearClipMarkers(clip);
            var a = vm.Editor.AddMarker(vm.Editor.Document, 1, "First");
            var b = vm.Editor.AddMarker(vm.Editor.Document, 2, "Middle");
            var c = vm.Editor.AddMarker(vm.Editor.Document, 3, "Last");
            vm.SelectedLibraryTab = vm.LibraryTabs.First(t => t.Title == "Markers");
            window.UpdateLayout();

            void ClickRow(string id, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .First(control => control.Classes.Contains("marker-row")
                        && control.DataContext is MarkerRowViewModel row && row.Id == id);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left, modifiers);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left, modifiers);
            }

            ClickRow(a.Id);
            ClickRow(c.Id, RawInputModifiers.Control);
            Assert.Equal(2, vm.Markers.SelectedCount);
            Assert.True(vm.Markers.SequenceMarkers.First(row => row.Id == a.Id).IsSelected);
            ClickRow(c.Id, RawInputModifiers.Shift);
            Assert.Equal(3, vm.Markers.SelectedCount);
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Empty(vm.Editor.Document.Markers);
            Assert.Equal(0, vm.Markers.SelectedCount);
            Assert.NotEmpty(vm.Editor.Document.Tracks.SelectMany(track => track.Clips));
            vm.Editor.Undo();
            Assert.Equal(new[] { a, b, c }, vm.Editor.Document.Markers);

            // Ctrl+A in the panel selects visible filtered results, not clips.
            vm.Markers.SearchText = "Middle";
            window.UpdateLayout();
            var panel = window.GetVisualDescendants().OfType<DockPanel>().First(control => control.Name == "MarkerPanelRoot");
            panel.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            Assert.Equal(new[] { b.Id }, vm.Editor.Selection.SelectedMarkerIds);
            Assert.Empty(vm.Editor.Selection.SelectedClipIds);
            var search = panel.GetVisualDescendants().OfType<TextBox>().First();
            search.Focus();
            search.SelectAll();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Equal(3, vm.Editor.Document.Markers.Count);
            Assert.Equal(new[] { b.Id }, vm.Editor.Selection.SelectedMarkerIds);
            Shot(window, "markers-multiselect");
        }
        finally
        {
            window.Close();
            vm.DisposePlayback();
        }
    }

    [AvaloniaFact]
    public void Timeline_MarkerModifierSelection_AndSelectAllDelete_KeepClips()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            // the sample project has a clip marker at 3 s too; with it left in, which marker a click at 3 s hits is up to its id
            vm.Editor.Document.Markers.Clear();
            foreach (var track in vm.Editor.Document.Tracks)
            {
                track.Markers.Clear();
                foreach (var clip in track.Clips)
                    clip.Markers.Clear();
            }
            var a = vm.Editor.AddMarker(vm.Editor.Document, 1);
            var b = vm.Editor.AddMarker(vm.Editor.Document, 2);
            var c = vm.Editor.AddMarker(vm.Editor.Document, 3);
            int clipCount = vm.Editor.Document.Tracks.Sum(track => track.Clips.Count);
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
            window.UpdateLayout();
            void Click(double time, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                // Center of the marker lane, after the track header.
                var point = timeline.TranslatePoint(new Point(TimelineView.TrackHeaderWidth + timeline.Viewport.TimeToX(time), 10), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left, modifiers);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left, modifiers);
            }
            Click(1);
            Click(3, RawInputModifiers.Control);
            Assert.Equal(2, vm.Editor.Selection.SelectedMarkerIds.Count);
            Click(3, RawInputModifiers.Shift);
            Assert.Equal(new[] { a.Id, b.Id, c.Id }, vm.Editor.Selection.SelectedMarkerIds);
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            Assert.Equal(vm.Editor.AllMarkers().Count(), vm.Editor.Selection.SelectedMarkerIds.Count);
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Empty(vm.Editor.AllMarkers());
            Assert.Equal(clipCount, vm.Editor.Document.Tracks.Sum(track => track.Clips.Count));
            vm.Editor.Undo();
            Assert.Equal(new[] { a, b, c }, vm.Editor.Document.Markers);
        }
        finally
        {
            window.Close();
            vm.DisposePlayback();
        }
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
    public void EditorView_Transform_StopwatchRecordsKeyframesAndNeverDeletesThem()
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

        vm.SeekFromUser(video.StartSec + 6);
        t.ToggleAnimateCommand.Execute(null);                        // stopwatch again: records here, the others stay
        Assert.True(t.IsAnimated);
        Assert.Equal(new[] { 4.0, 6.0, 8.0 }, fx.Keyframes["zoom"].Select(k => k.TimeSec).ToArray());

        t.ToggleAnimateCommand.Execute(null);                        // pressing where one already is changes nothing
        Assert.Equal(3, fx.Keyframes["zoom"].Count);

        t.ClearKeyframesCommand.Execute(null);                       // clearing is its own, explicit action
        Assert.False(t.IsAnimated);
        Assert.Empty(fx.Keyframes);

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
    public void EditorView_TrackEffects_ShareARowUnlessTheyOverlap()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var track = vm.Editor.Document.Tracks.First(t => t.Kind == Fig.Core.Timeline.TrackKind.Video);
            Fig.Core.Timeline.EffectInstance Add(double start, double dur)
            {
                var fx = Fig.Core.Media.EffectCatalog.Find("camera-shake")!.CreateInstance();
                fx.StartSec = start;
                fx.DurSec = dur;
                vm.Editor.AddEffect(track.Id, fx);
                return fx;
            }
            var a = Add(1, 0.5);
            var b = Add(3, 0.5);
            var c = Add(5, 0.5);
            var overlapsA = Add(1.25, 1);
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();

            double Y(Fig.Core.Timeline.EffectInstance fx) => timeline.RackPointFor(track.Id, fx.Id)!.Value.Y;
            Assert.Equal(Y(a), Y(b), 3);
            Assert.Equal(Y(a), Y(c), 3);
            Assert.True(Y(overlapsA) > Y(a) + 1, "an overlapping region needs its own row");
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void EditorView_TrackEffectCanCrossCutsAndDuplicateWithoutSelectingClips()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var video = vm.Editor.Document.Tracks.SelectMany(t => t.Clips).OfType<Fig.Core.Timeline.VideoClip>().First();
            var track = vm.Editor.FindTrackForClip(video);
            vm.Editor.Selection.SelectOnly(video.Id);
            vm.PlayheadTimeSec = 11;
            vm.ApplyPresetFromLibraryCommand.Execute(Fig.Core.Media.BuiltInEffectPresets.Find("zoom-in-out"));
            var effect = Assert.Single(track.Effects);
            Assert.Empty(video.Effects);
            Assert.Equal(11, effect.StartSec!.Value);
            Assert.True(effect.StartSec + effect.DurSec > video.StartSec + video.DurSec);
            Assert.Equal(track.Id, vm.Editor.Selection.SelectedEffect?.ClipId);
            Assert.Empty(vm.Editor.Selection.SelectedClipIds);
            Assert.Equal(effect.Id, vm.Properties.SelectedEffect?.EffectId);
            Assert.True(vm.Properties.Transform.IsActive);

            vm.Editor.Cut(video.Id, 11.5);
            Assert.Same(effect, Assert.Single(track.Effects));
            Assert.Equal(11, effect.Keyframes["zoom"][0].TimeSec);
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
            var key = timeline.RackPointFor(track.Id, effect.Id, 11)!.Value;
            var from = timeline.TranslatePoint(key + new Vector(12, -18), window)!.Value;
            var target = from + new Vector(50, 0);
            window.MouseDown(from, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(target, RawInputModifiers.Control);
            window.MouseUp(target, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
            Assert.Equal(2, track.Effects.Count);
            var copy = track.Effects.Single(fx => fx.Id != effect.Id);
            Assert.Equal(11.5, copy.StartSec!.Value, 6);
            Assert.Equal(effect.DurSec, copy.DurSec);
            Assert.Empty(vm.Editor.Selection.SelectedClipIds);
            timeline.Focus();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Same(effect, Assert.Single(track.Effects));
            Assert.NotEmpty(track.Clips);
            vm.Editor.Undo();
            Assert.Equal(2, track.Effects.Count);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }

    [AvaloniaFact]
    public void EditorView_ControlDragCopiesEffectBarsAndClips()
    {
        var vm = OpenSampleEditor(out var window);
        var (clip, fx) = AddMotion(vm, "shake-hit", 0.2);
        clip.DurSec = 2;
        foreach (var partner in vm.Editor.LinkGroup(clip.Id)) partner.DurSec = 2;
        window.UpdateLayout();
        var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
        Point ToWindow(Point p) => timeline.TranslatePoint(p, window)!.Value;
        var lane = timeline.RackPointFor(clip.Id, fx.Id)!.Value;
        // Label row rather than the keyframe diamonds below it.
        var from = ToWindow(lane - new Vector(0, 18));
        var target = from + new Vector(70, 0);
        window.MouseDown(from, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(target, RawInputModifiers.Control);
        Assert.Single(clip.Effects); // preview only until release
        window.MouseUp(target, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        var track = vm.Editor.FindTrackForClip(clip);
        var copy = Assert.Single(track.Effects);
        Assert.Single(clip.Effects);
        Assert.Equal(0.2, fx.StartSec!.Value, 6);
        Assert.Equal(0.9, copy.StartSec!.Value, 6);
        vm.Editor.Undo();
        Assert.Empty(track.Effects);
        Assert.Single(clip.Effects);

        // Video body above its rack; copy into free space three seconds later.
        from = ToWindow(new Point(TimelineView.TrackHeaderWidth + timeline.Viewport.TimeToX(0.5), lane.Y - 60));
        target = from + new Vector(300, 0);
        int before = vm.Editor.Document.Tracks.Sum(t => t.Clips.Count);
        window.MouseDown(from, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(target, RawInputModifiers.Control);
        Assert.Equal(before, vm.Editor.Document.Tracks.Sum(t => t.Clips.Count));
        window.MouseUp(target, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        Assert.Equal(before + 1, vm.Editor.Document.Tracks.Sum(t => t.Clips.Count));   // the video, its sound inside it
        Assert.Equal(0, clip.StartSec);
        Assert.Single(vm.Editor.Selection.SelectedClipIds);
        vm.Editor.Undo();
        Assert.Equal(before, vm.Editor.Document.Tracks.Sum(t => t.Clips.Count));
        window.Close();
        vm.DisposePlayback();
    }

    [AvaloniaFact]
    public void EditorView_EffectSelectionAndCopyLeaveClipUnselected()
    {
        var vm = OpenSampleEditor(out var window);
        var (clip, fx) = AddMotion(vm);
        window.UpdateLayout();
        var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
        Point ToWindow(Point p) => timeline.TranslatePoint(p, window)!.Value;
        var first = fx.Keyframes["zoom"][0].TimeSec;
        var diamond = ToWindow(timeline.RackPointFor(clip.Id, fx.Id, first)!.Value);
        window.MouseDown(diamond, Avalonia.Input.MouseButton.Left);
        window.MouseUp(diamond, Avalonia.Input.MouseButton.Left);
        Assert.Empty(vm.Editor.Selection.SelectedClipIds);
        Assert.Equal(fx.Id, vm.Editor.Selection.SelectedEffect?.EffectId);
        Assert.Equal(fx.Id, vm.Properties.SelectedEffect?.EffectId);

        var count = fx.Keyframes["zoom"].Count;
        var target = diamond + new Vector(50, 0);
        window.MouseDown(diamond, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(target, RawInputModifiers.Control);
        window.MouseUp(target, Avalonia.Input.MouseButton.Left, RawInputModifiers.Control);
        Assert.Equal(count + 1, fx.Keyframes["zoom"].Count);
        Assert.Contains(fx.Keyframes["zoom"], k => Math.Abs(k.TimeSec - first) < 1e-6);
        Assert.Empty(vm.Editor.Selection.SelectedClipIds);

        timeline.Focus();
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Assert.DoesNotContain(clip.Effects, effect => effect.Id == fx.Id);
        Assert.NotNull(vm.Editor.FindClipForView(clip.Id));
        vm.Editor.Undo();
        Assert.Contains(clip.Effects, effect => effect.Id == fx.Id);
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

    [AvaloniaFact]
    public void EditorView_TrimmingACutBetweenTwoClips_RollsTheCut_AndMovesNothingElse()
    {
        var vm = OpenSampleEditor(out var window);
        try
        {
            var track = vm.Editor.Document.Tracks.First(t => t.Kind == Fig.Core.Timeline.TrackKind.Video);
            var whole = track.Clips.First();
            var end = whole.StartSec + whole.DurSec;
            vm.Editor.Cut(whole.Id, 6);
            var (left, right) = (track.Clips[0], track.Clips[1]);
            var rightIn = right.SourceIn;
            window.UpdateLayout();
            var timeline = window.GetVisualDescendants().OfType<TimelineView>().First();
            Point ToWindow(Point p) => timeline.TranslatePoint(p, window)!.Value;
            var rect = timeline.ClipRectFor(left.Id)!.Value;
            // the lower part of the edge always trims (fades are grabbed in the top half)
            double Y() => rect.Bottom - 30;
            double X(double t) => TimelineView.TrackHeaderWidth + timeline.Viewport.TimeToX(t);

            void DragCut(double from, double to)
            {
                var a = ToWindow(new Point(X(from) - 2, Y()));
                var b = ToWindow(new Point(X(to) - 2, Y()));
                window.MouseDown(a, Avalonia.Input.MouseButton.Left);
                window.MouseMove(new Point((a.X + b.X) / 2, a.Y));
                window.MouseMove(b);
                window.MouseUp(b, Avalonia.Input.MouseButton.Left);
            }

            DragCut(6, 8);
            Assert.Equal(8, left.StartSec + left.DurSec, 1);
            Assert.Equal(left.StartSec + left.DurSec, right.StartSec, 6);   // still touching
            Assert.Equal(end, right.StartSec + right.DurSec, 6);             // its end stays put
            Assert.Equal(rightIn + (right.StartSec - 6), right.SourceIn, 6);  // it starts later in its file

            DragCut(right.StartSec, 4);
            Assert.Equal(4, right.StartSec, 1);
            Assert.Equal(right.StartSec, left.StartSec + left.DurSec, 6);
            Assert.Equal(end, right.StartSec + right.DurSec, 6);

            vm.Editor.Undo();
            vm.Editor.Undo();
            Assert.Equal((6.0, 6.0), (left.StartSec + left.DurSec, right.StartSec));
            Assert.Equal(rightIn, right.SourceIn, 6);
        }
        finally { window.Close(); vm.DisposePlayback(); }
    }
}
