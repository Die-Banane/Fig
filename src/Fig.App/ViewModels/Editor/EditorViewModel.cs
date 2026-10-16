using System.Collections.ObjectModel;
using System.Runtime.ConstrainedExecution;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.App.Services;
using Fig.App.Views;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Project;
using Fig.Core.Templates;
using Fig.Core.Timeline;
using ProjectModel = Fig.Core.Project.Project;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.App.ViewModels;

/// <summary>Left-rail sections in the editor library panel.</summary>
public enum LibraryPanelTab
{
    Media,
    Templates,
    Text,
    Transitions,
    Effects,
    Markers,
}

/// <summary>One vertical library tab (icon + label).</summary>
public sealed record LibraryTabItem(LibraryPanelTab Tab, string Icon, string Title);

/// <summary>
/// One surviving clip that shifted on the timeline. Draw at
/// <c>StartSec + FromOffsetSec</c> and animate the offset to zero.
/// </summary>
public readonly record struct RippleSlideDelta(string ClipId, double FromOffsetSec);

/// <summary>A single video layer to composite, ordered topmost-first.</summary>
public sealed class PreviewLayer
{
    public string SourcePath { get; }
    public double TimeSec { get; }
    public double Opacity { get; }
    /// <summary>A video clip (decoded from <see cref="SourcePath"/>) or a drawn layer (text, still image).</summary>
    public Fig.Core.Timeline.Clip Clip { get; }
    public int TrackIndex { get; }

    public PreviewLayer(string sourcePath, double timeSec, double opacity,
        Fig.Core.Timeline.Clip clip, int trackIndex)
    {
        SourcePath = sourcePath;
        TimeSec = timeSec;
        Opacity = opacity;
        Clip = clip;
        TrackIndex = trackIndex;
    }
}

public partial class EditorViewModel : ViewModelBase
{
    public GestureRegistry Gestures { get; }

    public ProjectModel? Project { get; private set; }
    public ProjectManager? ProjectManager { get; private set; }
    public TimelineEditor Editor { get; private set; }
    public PreviewViewModel Preview { get; }
    public PropertiesViewModel Properties { get; }
    public MarkersPanelViewModel Markers { get; }
    public BeatMarkersViewModel Beats { get; }
    public PlaybackEngine? Playback { get; private set; }
    private readonly MediaService _mediaService = new();

    public event Action<PlaybackEngine?>? PlaybackAssigned;
    public Action<string>? Notify { get; set; }

    /// <summary>Runs timeline exports as background jobs (progress shown in the jobs popup).</summary>
    public ExportJobRunner Exports { get; }

    [ObservableProperty]
    private bool _isJobsOpen;

    public bool HasExportJobs => Exports.Jobs.Count > 0;
    public bool ShowEmptyJobs => !HasExportJobs;
    public int ActiveExportCount => Exports.Jobs.Count(j => j.IsActive);
    /// <summary>Media filtered by the library search box.</summary>
    public System.Collections.ObjectModel.ObservableCollection<MediaAsset> VisibleMedia { get; } = new();

    [ObservableProperty]
    private string _mediaSearchText = "";

    public bool NoMediaMatches => HasMedia && VisibleMedia.Count == 0;

    partial void OnMediaSearchTextChanged(string value) => RefreshVisibleMedia();

    private void RefreshVisibleMedia()
    {
        var query = MediaSearchText.Trim();
        VisibleMedia.Clear();
        foreach (var asset in Media.Where(a => query.Length == 0
                     || a.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleMedia.Add(asset);
        OnPropertyChanged(nameof(NoMediaMatches));
    }

    public bool HasMedia => Media.Count > 0;
    public bool NoMedia => Media.Count == 0;

    /// <summary>
    /// Resolves the app's main window from the desktop lifetime. Used as a fallback owner
    /// for file pickers so commands work from any binding context (e.g. popup menu items
    /// where <c>$parent[Window]</c> cannot resolve).
    /// </summary>
    private static Window? MainWindow =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mw }
            ? mw
            : null;

    private readonly Dictionary<string, MediaAsset> _mediaById = new();
    private readonly object _importGate = new();
    private readonly Dictionary<string, Task<MediaAsset?>> _importsInFlight =
        new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, MediaAsset> MediaById => _mediaById;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<MediaAsset> _media = new();

    [ObservableProperty]
    private MediaAsset? _selectedMedia;

    [ObservableProperty]
    private string? _lastImportError;

    [ObservableProperty]
    private double _playheadTimeSec;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _magneticSnap = true;

    /// <summary>When playback reaches the end of the sequence it starts again from the beginning.</summary>
    [ObservableProperty]
    private bool _loopPlayback;

    /// <summary>Selecting a clip also selects the video or audio linked to it (Alt-click picks just one).</summary>
    [ObservableProperty]
    private bool _linkedSelection = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    private bool _isDirty;

    /// <summary>The last autosave failed; cleared by the next successful save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    private bool _saveFailed;

    /// <summary>Panel arrangement (sizes, collapsed panels, focus preview), remembered between sessions.</summary>
    public WorkspaceLayout Layout { get; } = new(AppPaths.WorkspaceLayoutPath);

    public string SaveStatusText => SaveFailed ? "Couldn't save · will retry on your next change"
        : IsDirty ? "Saving changes…" : "All changes saved";

    /// <summary>One line describing what is selected, shown in the status bar.</summary>
    [ObservableProperty]
    private string _selectionSummary = "Nothing selected";

    /// <summary>A hint that fits what the user is doing, shown in the status bar.</summary>
    [ObservableProperty]
    private string _statusHint = "Click a clip to select it · Space plays and pauses";

    /// <summary>Vertical library rail: Media, Transitions, Effects.</summary>
    public IReadOnlyList<LibraryTabItem> LibraryTabs { get; } =
    [
        new(LibraryPanelTab.Media, "film", "Media"),
        new(LibraryPanelTab.Templates, "clapperboard", "Templates"),
        new(LibraryPanelTab.Text, "type", "Text"),
        new(LibraryPanelTab.Transitions, "blend", "Transitions"),
        new(LibraryPanelTab.Effects, "wand-sparkles", "Effects"),
        new(LibraryPanelTab.Markers, "flag", "Markers"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryMedia))]
    [NotifyPropertyChangedFor(nameof(IsLibraryTemplates))]
    [NotifyPropertyChangedFor(nameof(IsLibraryText))]
    [NotifyPropertyChangedFor(nameof(IsLibraryTransitions))]
    [NotifyPropertyChangedFor(nameof(IsLibraryEffects))]
    [NotifyPropertyChangedFor(nameof(IsLibraryMarkers))]
    [NotifyPropertyChangedFor(nameof(LibraryPanelTitle))]
    [NotifyPropertyChangedFor(nameof(LibraryPanelCount))]
    private LibraryTabItem? _selectedLibraryTab;

    public bool IsLibraryMedia => SelectedLibraryTab?.Tab == LibraryPanelTab.Media;
    public bool IsLibraryTemplates => SelectedLibraryTab?.Tab == LibraryPanelTab.Templates;
    public bool IsLibraryText => SelectedLibraryTab?.Tab == LibraryPanelTab.Text;
    public bool IsLibraryTransitions => SelectedLibraryTab?.Tab == LibraryPanelTab.Transitions;
    public bool IsLibraryEffects => SelectedLibraryTab?.Tab == LibraryPanelTab.Effects;
    public bool IsLibraryMarkers => SelectedLibraryTab?.Tab == LibraryPanelTab.Markers;
    public string LibraryPanelTitle => SelectedLibraryTab?.Title ?? "Media";

    public IReadOnlyList<EffectCatalogEntry> EffectCatalogItems => EffectCatalog.All;
    public IReadOnlyList<TransitionCatalogEntry> TransitionCatalogItems => TransitionCatalog.All;
    public IReadOnlyList<TextPreset> TextPresetItems => TextPresets.All;

    /// <summary>Adds a library title at the playhead, on the active track when it has room there.</summary>
    [RelayCommand]
    private void AddText(TextPreset? preset)
        => Editor.AddText(Editor.Selection.ActiveTrackId, PlayheadTimeSec, preset ?? TextPresets.All[0]);

    // ---- effects / transitions browsers: search + category filtering ----
    // ---- presets: named, reusable keyframed effects (built in, plus the user's own) ----
    private readonly EffectPresetStore _presetStore = new(AppPaths.PresetsDir);
    private List<EffectPreset> _allPresets = new();
    public ObservableCollection<EffectPreset> FilteredPresets { get; } = new();
    public bool HasPresets => FilteredPresets.Count > 0;

    private void LoadPresets()
    {
        _allPresets = BuiltInEffectPresets.All.Concat(_presetStore.List()).ToList();
        RefreshPresetFilter();
    }

    private void RefreshPresetFilter()
    {
        var query = EffectSearchText.Trim();
        FilteredPresets.Clear();
        foreach (var preset in _allPresets.Where(p =>
                     (SelectedEffectCategory == "All" || (p.Category ?? EffectCatalog.Find(p.TypeId)?.Category) == SelectedEffectCategory)
                     && (query.Length == 0
                         || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))))
                FilteredPresets.Add(preset);
        OnPropertyChanged(nameof(HasPresets));
        OnPropertyChanged(nameof(NoEffectMatches));
    }

    private Track? EffectTargetTrack()
    {
        var ownerId = Editor.Selection.SelectedEffect?.ClipId ?? Editor.Selection.ActiveTrackId;
        var track = Editor.Document.Tracks.FirstOrDefault(t => t.Id == ownerId);
        if (track is null && Editor.Selection.SelectedClipIds.FirstOrDefault() is { } clipId)
            track = Editor.Document.Tracks.FirstOrDefault(t => t.Id == Editor.FindClipTrackId(clipId));
        return track?.Kind == TrackKind.Video ? track : null;
    }

    [RelayCommand]
    private void ApplyPresetFromLibrary(EffectPreset? preset)
    {
        if (preset is null || Editor is null)
            return;
        var track = EffectTargetTrack();
        if (track is null)
        {
            Notify?.Invoke("Select a video track or clip to apply this");
            return;
        }
        var start = Math.Max(0, Editor.SnapTime(PlayheadTimeSec));
        var instance = EffectPresetInstantiator.Create(preset, start + preset.DefaultDurationSec, start);
        Editor.AddEffect(track.Id, instance);
        Preview.RefreshFrame();
        Properties.SelectEffect(track.Id, instance.Id);
        Notify?.Invoke(preset.TypeId == EffectCatalog.Transform
            ? $"Added \"{preset.Name}\" \u2014 move the playhead and drag the frame on the preview to add more keyframes"
            : $"Added \"{preset.Name}\" \u2014 adjust its keyframes in the inspector");
    }

    [RelayCommand]
    private void DeletePreset(EffectPreset? preset)
    {
        if (preset is null || preset.BuiltIn)
            return;
        _presetStore.Delete(preset.Id);
        LoadPresets();
    }

    /// <summary>Asks for a name and saves an effect's bar and keyframes as a reusable preset.</summary>
    public async Task SaveEffectAsPresetAsync(string clipId, string effectId)
    {
        var clip = Editor.FindEffectOwner(clipId);
        var effect = clip?.Effects.FirstOrDefault(e => e.Id == effectId);
        if (clip is null || effect is null)
            return;
        var name = await Dialogs.PromptTextAsync(null, "Save as preset", "Preset name", "My preset", "Save", "My preset");
        if (name is null)
            return;
        var preset = EffectPresetInstantiator.FromEffect(effect, clip.DurSec, name);
        preset.Description = "Saved from your own edit.";
        _presetStore.Save(preset);
        LoadPresets();
        SelectedLibraryTab = LibraryTabs.First(t => t.Tab == LibraryPanelTab.Effects);
        SelectedEffectCategory = EffectCatalog.Find(effect.TypeId)?.Category ?? "All";
        Notify?.Invoke($"Saved \"{name}\" \u2014 find it under Effects \u2192 {SelectedEffectCategory}");
    }

    public IReadOnlyList<string> EffectCategories { get; } = ["All", "Motion", "Blur", "Light", "Glitch", "Retro", "Distort", "Color", "Stylize", GraphEffectLibrary.Category];
    public IReadOnlyList<string> TransitionCategories { get; } = ["All", "Basic", "Camera", "Blur", "Light", "Glitch", "Wipe"];
    [ObservableProperty] private string _selectedTransitionCategory = "All";
    public ObservableCollection<EffectCatalogEntry> FilteredEffects { get; } = new();
    public ObservableCollection<TransitionCatalogEntry> FilteredTransitions { get; } = new();

    [ObservableProperty] private string _effectSearchText = "";
    [ObservableProperty] private string _selectedEffectCategory = "All";
    [ObservableProperty] private string _transitionSearchText = "";

    public bool NoEffectMatches => FilteredEffects.Count == 0 && FilteredPresets.Count == 0;
    public bool NoTransitionMatches => FilteredTransitions.Count == 0;

    /// <summary>Number of items in the open library tab, shown beside its title (effects and transitions only).</summary>
    public string? LibraryPanelCount => SelectedLibraryTab?.Tab switch
    {
        LibraryPanelTab.Effects => EffectCatalogItems.Count.ToString(),
        LibraryPanelTab.Transitions => TransitionCatalogItems.Count.ToString(),
        _ => null,
    };

    [RelayCommand]
    private void SelectEffectCategory(string? category)
    {
        if (!string.IsNullOrEmpty(category))
            SelectedEffectCategory = category;
    }

    partial void OnEffectSearchTextChanged(string value) { RefreshEffectFilter(); RefreshPresetFilter(); }
    partial void OnSelectedEffectCategoryChanged(string value) { RefreshEffectFilter(); RefreshPresetFilter(); }
    partial void OnTransitionSearchTextChanged(string value) => RefreshTransitionFilter();
    partial void OnSelectedTransitionCategoryChanged(string value) => RefreshTransitionFilter();

    private void RefreshEffectFilter()
    {
        var query = EffectSearchText.Trim();
        FilteredEffects.Clear();
        foreach (var e in EffectCatalogItems.Where(e =>
                     (SelectedEffectCategory == "All" || e.Category == SelectedEffectCategory)
                     && (query.Length == 0
                         || e.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase))))
            FilteredEffects.Add(e);
        OnPropertyChanged(nameof(NoEffectMatches));
    }

    private void RefreshTransitionFilter()
    {
        var query = TransitionSearchText.Trim();
        FilteredTransitions.Clear();
        foreach (var t in TransitionCatalogItems.Where(t =>
                     (SelectedTransitionCategory == "All" || t.Category == SelectedTransitionCategory)
                     && (query.Length == 0
                     || t.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))))
            FilteredTransitions.Add(t);
        OnPropertyChanged(nameof(NoTransitionMatches));
    }

    private readonly TemplateStore _templateStore = new(AppPaths.TemplatesDir);
    public System.Collections.ObjectModel.ObservableCollection<VideoTemplate> Templates { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<VideoTemplate> FilteredTemplates { get; } = new();
    public IReadOnlyList<string> TemplateCategories { get; } =
        ["All", "My templates", "Social", "YouTube", "Business"];

    [ObservableProperty] private VideoTemplate? _selectedTemplate;
    [ObservableProperty] private string _templateSearchText = "";
    [ObservableProperty] private string _selectedTemplateCategory = "All";

    public bool HasTemplates => FilteredTemplates.Count > 0;
    public bool NoTemplates => !HasTemplates;

    partial void OnTemplateSearchTextChanged(string value) => RefreshTemplateFilter();
    partial void OnSelectedTemplateCategoryChanged(string value) => RefreshTemplateFilter();

    partial void OnSelectedLibraryTabChanged(LibraryTabItem? value)
    {
        // picking a rail tab while the panel is tucked away should show it
        if (value is not null && _constructed)
            Layout.RevealLeft();
    }

    partial void OnMagneticSnapChanged(bool value)
    {
        if (Editor is not null)
            Editor.MagneticSnap = value;
        Remember(p => p.Editing.Snap = value);
    }

    private bool _constructed;
    private ProjectStore? _store;
    private System.Threading.CancellationTokenSource? _autosaveCts;
    private readonly object _autosaveLock = new();

    public EditorViewModel(GestureRegistry gestures, PreferenceService? preferences = null)
    {
        Gestures = gestures;
        AttachPreferences(preferences ?? new PreferenceService());
        SelectedLibraryTab = LibraryTabs[0];
        RefreshEffectFilter();
        RefreshTransitionFilter();
        LoadPresets();
        LoadTemplates();
        Editor = CreateSeededEditor();
        Editor.MediaResolver = FindAssetById;
        Preview = new PreviewViewModel(_mediaService, ResolvePreviewLayers);
        Preview.Layers = Layers;
        Preview.Renderer = Preferences.Current.Rendering.Renderer;
        Preview.AttachEditor(this);
        Properties = new PropertiesViewModel(this);
        Markers = new MarkersPanelViewModel(this);
        Beats = new BeatMarkersViewModel(this);
        EffectGraphs.Changed += OnEffectGraphsChanged;
        Media.CollectionChanged += (_, _) =>
        {
            _mediaById.Clear();
            foreach (var asset in Media)
                _mediaById[asset.Id] = asset;
            OnPropertyChanged(nameof(MediaById));
            OnPropertyChanged(nameof(Media));
            OnPropertyChanged(nameof(SequenceEndSec));
            OnPropertyChanged(nameof(HasMedia));
            OnPropertyChanged(nameof(NoMedia));
            RefreshVisibleMedia();
            Preview.UpdateCanvasFromMedia();
            Properties.Refresh();
        };
        WireEditorEvents(Editor);
        InitializePlaybackForCurrentEditor();
        Properties.Refresh();

        Exports = new ExportJobRunner(_mediaService, () => Preferences.Current.Rendering.Renderer, Layers);
        Exports.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (ExportJob job in e.NewItems)
                    job.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ActiveExportCount));
            OnPropertyChanged(nameof(HasExportJobs));
            OnPropertyChanged(nameof(ShowEmptyJobs));
            OnPropertyChanged(nameof(ActiveExportCount));
        };
        _constructed = true;
    }

    private void LoadTemplates()
    {
        Templates.Clear();
        foreach (var template in BuiltInTemplateCatalog.All)
            Templates.Add(template);
        foreach (var template in _templateStore.List())
            Templates.Add(template);
        RefreshTemplateFilter();
    }

    private void RefreshTemplateFilter()
    {
        var query = TemplateSearchText.Trim();
        FilteredTemplates.Clear();
        foreach (var template in Templates.Where(template =>
                     (SelectedTemplateCategory == "All" || template.Category == SelectedTemplateCategory)
                     && (query.Length == 0
                         || template.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || template.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || template.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))))
        {
            FilteredTemplates.Add(template);
        }
        OnPropertyChanged(nameof(HasTemplates));
        OnPropertyChanged(nameof(NoTemplates));
    }

    /// <summary>Opens the transitions library (used by the inspector's "Replace").</summary>
    public void OpenTransitionsLibrary()
    {
        SelectedLibraryTab = LibraryTabs.First(t => t.Tab == LibraryPanelTab.Transitions);
        Layout.RevealLeft();
    }

    /// <summary>Opens the effects library (used by the inspector's "Add effect").</summary>
    [RelayCommand]
    private void OpenEffectsLibrary()
    {
        SelectedLibraryTab = LibraryTabs.First(t => t.Tab == LibraryPanelTab.Effects);
        Layout.RevealLeft();
    }

    [RelayCommand]
    private void CreateTemplate()
    {
        if (Project is null || !Editor.Document.Tracks.SelectMany(track => track.Clips).Any())
        {
            Notify?.Invoke("Add clips before saving a template");
            return;
        }

        var template = TemplateBuilder.FromTimeline(
            $"{Project.Name} template",
            "My templates",
            Editor.Document,
            Media,
            "Reusable edit created from the current timeline.");
        _templateStore.Save(template);
        LoadTemplates();
        SelectedLibraryTab = LibraryTabs.First(tab => tab.Tab == LibraryPanelTab.Templates);
        SelectedTemplateCategory = "My templates";
        SelectedTemplate = template;
        Notify?.Invoke($"Saved template \"{template.Name}\"");
    }

    [RelayCommand]
    private void UseTemplate(VideoTemplate? template)
    {
        if (template is null || Project is null)
            return;
        if (Editor.Document.Tracks.SelectMany(track => track.Clips).Any())
        {
            Notify?.Invoke("Templates can be applied to an empty timeline");
            return;
        }

        var available = Media.ToList();
        var assignments = new Dictionary<string, MediaAsset>();
        foreach (var slot in template.Slots)
        {
            var asset = available.FirstOrDefault(candidate =>
                candidate.Kind == slot.Kind || slot.Kind == MediaKind.Video && candidate.Kind == MediaKind.Image);
            if (asset is null)
            {
                Notify?.Invoke($"Import {template.SlotLabel} with compatible media first");
                return;
            }
            assignments[slot.Id] = asset;
            available.Remove(asset);
        }

        var timeline = TemplateInstantiator.Instantiate(template, assignments);
        Project.Timelines[0] = timeline;
        ActivateTimeline(timeline);
        _store?.SaveProject(Project);
        Notify?.Invoke($"Applied \"{template.Name}\"");
    }

    private void WireEditorEvents(TimelineEditor editor)
    {
        editor.TimelineChanged += OnEditorTimelineChanged;
        editor.TimelineChanged += ScheduleAutosave;
        editor.Selection.Changed += OnSelectionChanged;
    }

    private void UnwireEditorEvents(TimelineEditor editor)
    {
        editor.TimelineChanged -= OnEditorTimelineChanged;
        editor.TimelineChanged -= ScheduleAutosave;
        editor.Selection.Changed -= OnSelectionChanged;
    }

    private bool _syncingSelection;

    private void OnSelectionChanged()
    {
        if (_syncingSelection)
            return;
        if (!Editor.Selection.IsEmpty)
        {
            _syncingSelection = true;
            SelectedMedia = null;
            _syncingSelection = false;
        }
        Properties.Refresh();
        UpdateSelectionSummary();
        Markers.RefreshSelection();
    }

    private void UpdateSelectionSummary()
        => (SelectionSummary, StatusHint) = Editor.DescribeSelection();

    /// <summary>
    /// Deletes whatever is selected (the Delete key). Each kind of thing knows how to be deleted; this only
    /// reports what happened. Returns false when there was nothing to delete.
    /// </summary>
    public bool DeleteSelection()
    {
        if (Editor.DeleteSelection() is not { } removed)
            return false;
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke(removed.Count == 1 ? $"Deleted 1 {removed.Noun}" : $"Deleted {removed.Count} {removed.Noun}s");
        return true;
    }

    private void OnEditorTimelineChanged()
    {
        Beats.OnTimelineChanged();
        IsDirty = true;
        OnPropertyChanged(nameof(SequenceEndSec));
        Properties.Refresh();
        Markers.Refresh();
        // property edits (opacity/crop) need an immediate preview redraw
        if (!IsPlaying)
            Preview.RefreshFrame();
    }

    partial void OnSelectedMediaChanged(MediaAsset? value)
    {
        if (!_syncingSelection
            && value is not null
            && (Editor.Selection.Count > 0 || Editor.Selection.ActiveTrackId is not null))
        {
            _syncingSelection = true;
            Editor.Selection.Clear();
            _syncingSelection = false;
        }
        Properties.Refresh();
    }

    /// <summary>Called when derived media artifacts (proxy/filmstrip) change so UI can refresh.</summary>
    public void NotifyMediaArtifactsChanged()
    {
        OnPropertyChanged(nameof(MediaById));
        Editor.NotifyMediaChanged();
        Properties.Refresh();
        if (Project is not null)
            _store?.SaveProject(Project);
    }

    public double SequenceEndSec
    {
        get
        {
            double end = 0;
            foreach (var track in Editor.Document.Tracks)
                foreach (var clip in track.Clips)
                    end = Math.Max(end, clip.StartSec + clip.DurSec);
            return end;
        }
    }

    public double FrameDurationSec => 1.0 / Math.Max(Editor.Document.Rate.Fps, 1);

    /// <summary>Timeline scrub or transport seek (not driven by playback clock).</summary>
    public void SeekFromUser(double sec)
    {
        StopShuttle();
        MovePlayhead(sec, audible: true);
    }

    /// <summary>Moves the playhead; while stopped, scrubbing also plays the audio under it.</summary>
    private void MovePlayhead(double sec, bool audible)
    {
        _stopAtSec = null;
        PlayheadTimeSec = Math.Max(0, sec);
        Playback?.Seek(PlayheadTimeSec);
        if (audible && AudioScrub)
            Playback?.ScrubTo(PlayheadTimeSec);
        Preview.PlayheadSec = PlayheadTimeSec;
    }

    private double? _stopAtSec;

    /// <summary>Plays from <paramref name="startSec"/> and pauses when it reaches <paramref name="endSec"/>.</summary>
    public async void PlayRange(double startSec, double endSec)
    {
        if (Playback is null)
            return;
        if (Playback.IsPlaying)
            await Playback.PauseAsync();
        _stopAtSec = null;
        MovePlayhead(startSec, audible: false);
        _stopAtSec = endSec;
        IsPlaying = true;
        Preview.PreRollPlayback();
        await Playback.PlayAsync();
        IsPlaying = Playback.IsPlaying;
    }

    /// <summary>Audio master clock position during playback.</summary>
    public void NotifyPlaybackPosition(double sec)
    {
        if (_stopAtSec is { } stop && sec >= stop)
        {
            _stopAtSec = null;
            _ = Playback?.PauseAsync();
        }
        PlayheadTimeSec = Math.Max(0, sec);
        Preview.PlayheadSec = PlayheadTimeSec;
        IsPlaying = Playback?.IsPlaying ?? false;
    }

    /// <summary>
    /// Collects every visible, enabled video clip covering a timeline time, ordered
    /// topmost-first (the last track drawn on top is the top layer, matching the project
    /// thumbnail's painters algorithm), so the compositor can layer them painters-style.
    /// Returns an empty list when nothing is shown.
    /// </summary>
    private IReadOnlyList<PreviewLayer> ResolvePreviewLayers(double timeSec)
    {
        var layers = new List<PreviewLayer>();
        if (Editor is null || MediaById.Count == 0)
            return layers;

        var document = Editor.Document;

        // The compositor treats layers[0] as the topmost (blended last). The last track is
        // the top layer, so collect tracks in reverse order to make it win.
        for (var i = document.Tracks.Count - 1; i >= 0; i--)
        {
            var track = document.Tracks[i];
            if (track.Kind != TrackKind.Video || !track.Visible)
                continue;

            foreach (var clip in track.Clips)
            {
                if (!clip.Enabled || timeSec < clip.StartSec || timeSec >= clip.StartSec + clip.DurSec)
                    continue;
                if (clip is ILayerClip)
                {
                    // drawn, not decoded: the preview asks the rasterizer for it
                    var layerT = timeSec - clip.StartSec;
                    layers.Add(new PreviewLayer("", layerT, ClipFade.EffectiveOpacity(clip, layerT), clip, i));
                    continue;
                }
                if (clip is not VideoClip vc)
                    continue;

                if (!MediaById.TryGetValue(vc.SourceId, out var asset) || string.IsNullOrEmpty(asset.Url) || asset.Offline)
                    continue;

                var ratio = vc.SourceRate is { } r ? r.Fps / document.Rate.Fps : 1.0;
                var srcTime = vc.SrcInSec + (timeSec - clip.StartSec) * vc.Speed * ratio;
                var localT = timeSec - clip.StartSec;
                var opacity = ClipFade.EffectiveOpacity(vc, localT);
                layers.Add(new PreviewLayer(asset.PlaybackVideoPath, srcTime, opacity, vc, i));
            }
        }
        return layers;
    }

    [RelayCommand]
    private void ApplyEffectFromCatalog(EffectCatalogEntry? entry)
    {
        if (entry is null || Editor is null)
            return;
        var track = EffectTargetTrack();
        if (track is null)
        {
            Notify?.Invoke("Select a video track or clip to apply an effect");
            return;
        }
        var instance = entry.CreateInstance();
        instance.StartSec = Math.Max(0, Editor.SnapTime(PlayheadTimeSec));
        instance.DurSec = 3;
        Editor.AddEffect(track.Id, instance);
        Preview.RefreshFrame();
        Properties.SelectEffect(track.Id, instance.Id);
        Notify?.Invoke(entry.TypeId == EffectCatalog.Transform
            ? "Added Transform \u2014 press the stopwatch to animate it with keyframes"
            : $"Added {entry.DisplayName}");
    }

    // ---- effect graphs ---------------------------------------------------------------------

    /// <summary>The effects the user built from nodes in this project.</summary>
    public GraphEffectLibrary EffectGraphs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGraphEditorOpen))]
    private GraphEditorViewModel? _graphEditor;

    public bool IsGraphEditorOpen => GraphEditor is not null;

    /// <summary>Starts a new effect: an Input wired straight to an Output, ready to build on.</summary>
    [RelayCommand]
    private void NewGraphEffect()
    {
        var graph = new EffectGraph { Name = UniqueGraphName("New effect") };
        var input = new GraphNode { Id = "input", PrimitiveId = PrimitiveCatalog.Input, X = 40, Y = 120 };
        var output = new GraphNode { Id = "output", PrimitiveId = PrimitiveCatalog.Output, X = 520, Y = 120 };
        graph.Nodes.Add(input);
        graph.Nodes.Add(output);
        graph.Edges.Add(new GraphEdge { FromNodeId = "input", FromPort = "image", ToNodeId = "output", ToPort = "image" });
        OpenGraphEditor(graph);
    }

    /// <summary>Opens a saved graph effect (by its catalog type id) in the graph editor.</summary>
    public void EditGraphEffect(string typeId)
    {
        var saved = EffectGraphs.Graphs.FirstOrDefault(g => g.TypeId == typeId);
        if (saved is null)
        {
            Notify?.Invoke("That effect is not part of this project.");
            return;
        }
        OpenGraphEditor(saved.Clone());
    }

    [RelayCommand]
    private void EditGraphEffectFromCatalog(EffectCatalogEntry? entry)
    {
        if (entry is { IsCustom: true })
            EditGraphEffect(entry.TypeId);
    }

    [RelayCommand]
    private void DuplicateGraphEffect(EffectCatalogEntry? entry)
    {
        if (entry is not { IsCustom: true })
            return;
        var id = entry.TypeId[EffectGraph.TypeIdPrefix.Length..];
        if (EffectGraphs.Duplicate(id) is { } copy)
            Notify?.Invoke($"Created \"{copy.Name}\"");
    }

    [RelayCommand]
    private void DeleteGraphEffect(EffectCatalogEntry? entry)
    {
        if (entry is not { IsCustom: true })
            return;
        var id = entry.TypeId[EffectGraph.TypeIdPrefix.Length..];
        var uses = Editor.Document.Tracks.Sum(t => t.Effects.Count(e => e.TypeId == entry.TypeId))
                   + Editor.Document.Tracks.SelectMany(t => t.Clips).Sum(c => c.Effects.Count(e => e.TypeId == entry.TypeId));
        EffectGraphs.Delete(id);
        Preview.RefreshFrame();
        Notify?.Invoke(uses > 0
            ? $"Deleted \"{entry.DisplayName}\". {uses} use{(uses == 1 ? "" : "s")} on the timeline will show the original picture."
            : $"Deleted \"{entry.DisplayName}\"");
    }

    private void OpenGraphEditor(EffectGraph graph)
    {
        var editor = new GraphEditorViewModel(EffectGraphs, graph, message => Notify?.Invoke(message));
        editor.CloseRequested += () =>
        {
            GraphEditor = null;
            Preview.RefreshFrame();
            Properties.Refresh();
        };
        GraphEditor = editor;
    }

    private string UniqueGraphName(string baseName)
    {
        var name = baseName;
        for (var i = 2; EffectGraphs.Graphs.Any(g => g.Name == name); i++)
            name = $"{baseName} {i}";
        return name;
    }

    /// <summary>Called whenever the library changes: refresh the catalog lists, the preview and the autosave.</summary>
    private void OnEffectGraphsChanged()
    {
        OnPropertyChanged(nameof(EffectCatalogItems));
        RefreshEffectFilter();
        Properties?.Refresh();
        Preview?.RefreshFrame();
        IsDirty = true;
        ScheduleAutosave();
    }

    [RelayCommand]
    private void ApplyTransitionFromCatalog(TransitionCatalogEntry? entry)
    {
        if (entry is null || Editor is null)
            return;
        var clipId = Editor.Selection.SelectedClipIds.FirstOrDefault();
        if (clipId is null)
        {
            Notify?.Invoke("Select the outgoing clip at a cut");
            return;
        }
        if (!Editor.TryApplyTransitionFromSelection(clipId, entry.CreateRef()))
        {
            Notify?.Invoke(Editor.TransitionValidationMessage ?? "Could not apply transition");
            return;
        }
        Preview.RefreshFrame();
        Properties.Refresh();
        Notify?.Invoke($"Applied {entry.DisplayName}");
    }

    private void ScheduleAutosave()
    {
        if (_store is null || Project is null)
            return;

        lock (_autosaveLock)
        {
            _autosaveCts?.Cancel();
            _autosaveCts = new System.Threading.CancellationTokenSource();
        }

        _ = AutosaveAfterDelayAsync(_autosaveCts.Token);
    }

    private async Task AutosaveAfterDelayAsync(System.Threading.CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Preferences.Current.General.AutosaveSeconds), token);
            if (token.IsCancellationRequested)
                return;

            await System.Threading.Tasks.Task.Run(() =>
            {
                lock (_autosaveLock)
                {
                    if (_store is not null && Project is not null)
                        _store.SaveProject(Project!);
                }
            });

            // saved. If another edit arrived meanwhile its own autosave is pending, so stay "saving".
            if (!token.IsCancellationRequested)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SaveFailed = false;
                    IsDirty = false;
                });
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SaveFailed = true);
        }
    }

    public void LoadProject(Project project, ProjectStore store)
    {
        _store = store;
        EffectMigration.Migrate(project);
        EffectMigration.MoveClipEffectsToTracks(project);
        Project = project;
        GraphEditor = null;
        EffectGraphs.Attach(project.EffectGraphs);
        ProjectManager = new ProjectManager(project, new MediaService(), store.CacheDirectory(project.Id));

        Media.Clear();
        foreach (var asset in project.Media)
            Media.Add(asset);

        if (project.Timelines.Count == 0)
            project.Timelines.Add(new TimelineModel { Rate = FrameRate.Common(30) });

        ActivateTimeline(project.Timelines[0]);
    }

    private void ActivateTimeline(TimelineModel timeline)
    {
        Beats.Cancel();
        UnwireEditorEvents(Editor);
        Editor = new TimelineEditor(timeline, Preferences.Current.Editing.UndoSteps);
        Editor.MediaResolver = FindAssetById;
        Editor.MagneticSnap = MagneticSnap;
        Editor.RefreshTrackIndices();
        WireEditorEvents(Editor);
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(SequenceEndSec));
        OnPropertyChanged(nameof(FrameDurationSec));

        SelectedMedia = null;
        InitializePlaybackForCurrentEditor();
        SeekFromUser(0);
        Properties.Refresh();
    }

    private void InitializePlaybackForCurrentEditor()
    {
        if (Playback is not null)
        {
            Playback.PositionChanged -= OnPlaybackPositionChanged;
            Playback.StateChanged -= OnPlaybackStateChanged;
        }
        Playback?.Dispose();
        Playback = new PlaybackEngine(Editor, _mediaService, sourceId => FindAssetById(sourceId));
        Playback.PositionChanged += OnPlaybackPositionChanged;
        Playback.StateChanged += OnPlaybackStateChanged;
        Preview.AttachPlayback(Playback);
        OnPropertyChanged(nameof(Playback));
        PlaybackAssigned?.Invoke(Playback);
    }

    /// <summary>Stops playback and frees the device when leaving the editor (e.g. closing the project).</summary>
    public void DisposePlayback()
    {
        if (Playback is not null)
        {
            Playback.PositionChanged -= OnPlaybackPositionChanged;
            Playback.StateChanged -= OnPlaybackStateChanged;
        }
        Playback?.Dispose();
        Playback = null;
        Preview.AttachPlayback(null);
        PlaybackAssigned?.Invoke(null);
        OnPropertyChanged(nameof(Playback));
    }

    private void OnPlaybackPositionChanged(double sec) => NotifyPlaybackPosition(sec);
    private void OnPlaybackStateChanged(PlaybackState state)
    {
        IsPlaying = state is PlaybackState.Buffering or PlaybackState.Playing;
        if (state == PlaybackState.Ended && LoopPlayback)
            _ = TogglePlaybackAsync();
        if (state == PlaybackState.Faulted && Playback?.Error is { Length: > 0 } error)
            Notify?.Invoke(error);
        else if (state == PlaybackState.Unavailable)
            Notify?.Invoke("No audio output device is available.");
    }

    private MediaAsset? FindAssetById(string sourceId)
    {
        return MediaById.TryGetValue(sourceId, out var asset) ? asset : null;
    }

    public async Task<ProjectValidationReport> ValidateProjectAsync(Action<string>? progress = null)
    {
        var manager = ProjectManager;
        if (manager is null)
            return new ProjectValidationReport();

        var report = await Task.Run(() => manager.ValidateAndRepair(progress));

        if (report.HadIssues)
        {
            OnPropertyChanged(nameof(MediaById));
            Editor.NotifyMediaChanged();
            _store?.SaveProject(Project!);
        }

        return report;
    }

    /// <summary>
    /// Generates missing filmstrips / waveforms on a background thread so project open
    /// and import stay responsive. Safe to call repeatedly.
    /// </summary>
    public async Task BackfillMediaPreviewsAsync()
    {
        var manager = ProjectManager;
        if (manager is null)
            return;

        var pending = Media.Where(ProjectManager.NeedsPreviewBackfill).ToList();
        if (pending.Count == 0)
            return;

        await Task.Run(() =>
        {
            foreach (var asset in pending)
            {
                try
                {
                    manager.FinalizeMediaArtifacts(asset);
                }
                catch
                {
                    // leave the asset without previews; timeline still works
                }
            }
        });

        // proxy paths may have changed — reopen decoders on next frame
        Preview.InvalidateSources();
        NotifyMediaArtifactsChanged();
        if (Project is not null)
            _store?.SaveProject(Project);
    }

    [RelayCommand]
    private async Task ImportAsync(Window? owner)
    {
        owner ??= MainWindow;
        if (owner is null || ProjectManager is null)
            return;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Media",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media")
                {
                    Patterns = new[]
                    {
                        "*.mp4", "*.webm", "*.mov", "*.mkv", "*.avi", "*.m4v",
                        "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a", "*.aac", "*.wma",
                        "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp",
                    },
                },
            },
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null)
                continue;

            // probe + hash + thumbnail are heavy (SHA-256 of the whole file) — never run
            // them on the UI thread or importing a large video freezes the window
            ProbeResult result;
            try
            {
                result = await System.Threading.Tasks.Task.Run(() => ProjectManager.ImportMedia(path));
            }
            catch (Exception ex)
            {
                result = new ProbeResult { Error = ex.Message };
            }

            if (result.Asset is not null)
            {
                // the card appears immediately once the probe lands on the UI thread
                if (!Media.Contains(result.Asset))
                    Media.Add(result.Asset);

                // slow: filmstrip + waveform + proxy on a background thread; previews pop in when done
                var asset = result.Asset;
                _ = System.Threading.Tasks.Task.Run(() => ProjectManager.FinalizeMediaArtifacts(asset, _ =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Preview.InvalidateSources();
                        NotifyMediaArtifactsChanged();
                    });
                }));
            }
            else
            {
                LastImportError = result.Error;
            }
        }

        _store?.SaveProject(Project!);
    }

    /// <summary>
    /// Imports one file and returns the media asset. Side-effect: adds to the library and
    /// kicks off backfill work (filmstrip/waveform). Used by file-drop handlers. The heavy
    /// probe/hash/thumbnail work runs off the UI thread; awaiting returns once the asset is
    /// ready to be placed on the timeline.
    /// </summary>
    public Task<MediaAsset?> ImportFileAsync(string path)
    {
        var importPath = NormalizeImportPath(path);
        lock (_importGate)
        {
            var existingAsset = Media.FirstOrDefault(asset =>
                string.Equals(NormalizeImportPath(asset.Url), importPath, StringComparison.OrdinalIgnoreCase));
            if (existingAsset is not null)
                return Task.FromResult<MediaAsset?>(existingAsset);

            if (_importsInFlight.TryGetValue(importPath, out var existingImport))
                return existingImport;

            var import = ImportFileCoreAsync(importPath);
            _importsInFlight[importPath] = import;
            _ = RemoveCompletedImportAsync(importPath, import);
            return import;
        }
    }

    private async Task<MediaAsset?> ImportFileCoreAsync(string path)
    {
        if (ProjectManager is null)
            return null;

        ProbeResult result;
        try
        {
            result = await System.Threading.Tasks.Task.Run(() => ProjectManager.ImportMedia(path));
        }
        catch
        {
            return null;
        }

        if (result.Asset is null)
            return null;
        if (!Media.Contains(result.Asset))
            Media.Add(result.Asset);
        var asset = result.Asset;
        _ = System.Threading.Tasks.Task.Run(() =>
            ProjectManager.FinalizeMediaArtifacts(asset, _ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Preview.InvalidateSources();
                    NotifyMediaArtifactsChanged();
                });
            }));
        _store?.SaveProject(Project!);
        return asset;
    }

    private async Task RemoveCompletedImportAsync(string path, Task<MediaAsset?> import)
    {
        try
        {
            await import;
        }
        finally
        {
            lock (_importGate)
            {
                if (_importsInFlight.TryGetValue(path, out var current) && ReferenceEquals(current, import))
                    _importsInFlight.Remove(path);
            }
        }
    }

    private static string NormalizeImportPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    [RelayCommand]
    private void Save() => SaveWithThumbnail();

    /// <summary>Public entry point used by the close-project dialog and other callers.</summary>
    public void SaveNow() => SaveWithThumbnail();

    private void SaveWithThumbnail()
    {
        if (_store is null || Project is null)
            return;
        var manager = ProjectManager;
        var project = Project;
        var name = project.Name;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            // render the first composited frame as the project card thumbnail
            try
            {
                manager?.UpdateProjectThumbnail();
            }
            catch
            {
            }
            lock (_autosaveLock)
            {
                _store?.SaveProject(project);
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsDirty = false);
            Notify?.Invoke($"Saved \"{name}\"");
        });
    }

    [RelayCommand]
    private async Task SaveAsAsync(Window? owner)
    {
        owner ??= MainWindow;
        if (owner is null || Project is null)
            return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Project As",
            SuggestedFileName = $"{Project.Name}.fig.json",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Fig project") { Patterns = new[] { "*.json" } },
            },
        });
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (path is null)
            return;

        // treat Save As as also renaming the project to the chosen file stem
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        if (stem.EndsWith(".fig", StringComparison.OrdinalIgnoreCase))
            stem = System.IO.Path.GetFileNameWithoutExtension(stem);
        if (!string.IsNullOrWhiteSpace(stem))
        {
            Project.Name = stem.Trim();
            OnPropertyChanged(nameof(Project));
        }

        try
        {
            ProjectManager?.UpdateProjectThumbnail();
        }
        catch
        {
        }
        new SaveService(path).Save(Project!);
        _store?.SaveProject(Project!);
        SaveFailed = false;
        IsDirty = false;
        Notify?.Invoke($"Saved \"{Project.Name}\"");
    }

    /// <summary>
    /// Raised after an edit that slid surviving clips (ripple delete, and undo/redo of such).
    /// <see cref="RippleSlideDelta.FromOffsetSec"/> is oldStart − newStart so the view can
    /// draw at <c>StartSec + offset</c> and lerp offset → 0.
    /// </summary>
    public event Action<IReadOnlyList<RippleSlideDelta>>? RippleSlideStarted;

    [RelayCommand]
    private void Undo() => RunWithRippleSlide(() => Editor.Undo());

    [RelayCommand]
    private void ToggleMagneticSnap() => MagneticSnap = !MagneticSnap;

    [RelayCommand]
    private void ToggleLoop() => LoopPlayback = !LoopPlayback;

    [RelayCommand]
    private void CopySelected()
    {
        if (Editor.CopySelected())
            Notify?.Invoke(Editor.Selection.Count == 1 ? "Copied clip" : $"Copied {Editor.Selection.Count} clips");
    }

    [RelayCommand]
    private void CutSelected() => Editor.CutSelected();

    [RelayCommand]
    private void Paste() => Editor.Paste(PlayheadTimeSec, Editor.Selection.ActiveTrackId);

    [RelayCommand]
    private void DuplicateSelected() => Editor.DuplicateSelected();

    [RelayCommand]
    private void Redo() => RunWithRippleSlide(() => Editor.Redo());

    [RelayCommand]
    private void SplitAtPlayhead()
    {
        // selection wins: only selected (+ linked) clips are cut. if nothing is selected,
        // fall back to the active track's clip under the playhead — never every track.
        if (Editor.Selection.Count > 0)
        {
            Editor.SplitAtPlayhead(PlayheadTimeSec);
            return;
        }

        var trackId = Editor.Selection.ActiveTrackId
            ?? Editor.Document.Tracks.FirstOrDefault()?.Id;
        if (trackId is null)
            return;
        Editor.SplitAtPlayhead(trackId, PlayheadTimeSec);
    }

    [RelayCommand]
    private void RippleDeleteSelected()
    {
        // snapshot before the core mutates StartSec — only survivors that moved get deltas
        RunWithRippleSlide(() => Editor.RippleDeleteSelected());
    }

    [RelayCommand]
    private void LiftSelected()
    {
        Editor.LiftSelected();
        Properties.Refresh();
        Preview.RefreshFrame();
    }




    /// <summary>
    /// Adds a marker at the playhead. Attaches to the selected clip (local offset), the
    /// active track, or the timeline, in that order of context. Selects the new marker.
    /// </summary>
    [RelayCommand]
    private void AddMarkerAtPlayhead()
    {
        var time = Editor.SnapTime(PlayheadTimeSec);
        Marker? marker;
        if (Editor.Selection.SelectedClipIds.FirstOrDefault() is { } clipId)
        {
            var clip = Editor.Document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            marker = clip is not null ? Editor.AddMarker(clip, time - clip.StartSec) : null;
        }
        else if (Editor.Selection.ActiveTrackId is { } trackId)
        {
            var track = Editor.Document.Tracks.FirstOrDefault(t => t.Id == trackId);
            marker = track is not null ? Editor.AddMarker(track, time) : null;
        }
        else
        {
            marker = Editor.AddMarker(Editor.Document, time);
        }

        if (marker is null)
            return;
        Editor.Selection.SelectMarker(marker.Id);
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke(marker.Name.Length == 0 ? "Added marker at playhead" : $"Added marker \"{marker.Name}\"");
    }

    /// <summary>
    /// Asks how to build ranges from the timeline's bookmarks (shows a preview), then adds them.
    /// Returns true when ranges were created.
    /// </summary>
    public async Task<bool> CreateRangesFromBookmarksAsync(Window? owner)
    {
        owner ??= MainWindow;
        if (owner is null)
            return false;
        var dialog = new BookmarkRangesDialog(Editor.Document);
        var includeBounds = await dialog.ShowDialog<bool?>(owner);
        if (includeBounds is null)
            return false;
        var created = Editor.CreateRangesFromBookmarks(includeBounds.Value);
        if (created.Count == 0)
            return false;
        Notify?.Invoke(created.Count == 1 ? "Created 1 range" : $"Created {created.Count} ranges");
        return true;
    }

    [RelayCommand]
    private async Task CreateRangesFromBookmarks(Window? owner) => await CreateRangesFromBookmarksAsync(owner);

    /// <summary>Selects a marker (from the Markers panel) and moves the playhead to it.</summary>
    public void SelectMarker(string markerId)
    {
        if (Editor.FindMarker(markerId) is not { } loc)
            return;
        Editor.Selection.SelectMarker(markerId);
        SeekFromUser((loc.Clip?.StartSec ?? 0) + loc.Marker.StartSec);
        Preview.RefreshFrame();
    }

    /// <summary>Deletes a marker by id.</summary>
    public void DeleteMarker(string markerId) => Editor.DeleteMarker(markerId);

    public void DeleteMarkerSelectionOrOne(string markerId)
    {
        if (Editor.Selection.IsMarkerSelected(markerId))
            DeleteSelectedMarker();
        else
            DeleteMarker(markerId);
    }

    /// <summary>Opens the inspector on the marker so it can be renamed, recolored or retimed.</summary>
    public void EditMarker(string markerId)
    {
        Editor.Selection.SelectMarker(markerId);
        Layout.RevealRight();
    }


    /// <summary>Analysis for the beat-marker session; the view model owns the analysis, not the host.</summary>
    internal BeatAnalysisService CreateBeatAnalyzer() => new(_mediaService);

    internal MediaAsset? FindAsset(string sourceId) => FindAssetById(sourceId);

    public void UseMarkerAsRangeStart(string markerId) => AddRangeFromBookmark(markerId, true);

    public void UseMarkerAsRangeEnd(string markerId) => AddRangeFromBookmark(markerId, false);

    private void AddRangeFromBookmark(string markerId, bool asStart)
    {
        var range = Editor.AddRangeFromBookmark(markerId, asStart);
        if (range is null)
        {
            Notify?.Invoke("There's no room for a range here — only bookmarks on the sequence can start or end one");
            return;
        }
        Editor.Selection.SelectMarker(range.Id);
        Notify?.Invoke($"Created range \"{range.Name}\"");
    }

    /// <summary>True when an effect is selected, so a keyframe can be placed at a marker.</summary>
    public bool CanPlaceKeyframeAtMarker => Properties.SelectedEffect is not null;

    /// <summary>Moves the playhead to the marker and keyframes the selected effect there.</summary>
    public void PlaceKeyframeAtMarker(string markerId)
    {
        Editor.Selection.SelectMarker(markerId);
        Properties.PlaceKeyframeAtMarkerCommand.Execute(null);
    }

    /// <summary>Adds a bookmark (point marker) to the sequence at the playhead.</summary>
    public void AddTimelinePointMarker()
    {
        var count = Editor.Document.Markers.Count(m => !m.IsRange) + 1;
        var marker = Editor.AddMarker(Editor.Document, Editor.SnapTime(PlayheadTimeSec), $"Marker {count}", "#eab308");
        Editor.Selection.SelectMarker(marker.Id);
        Notify?.Invoke($"Added \"{marker.Name}\" at the playhead");
    }

    /// <summary>Adds a range marker starting at the playhead (up to 4 seconds, or until the end of the sequence).</summary>
    public void AddTimelineRangeMarker()
    {
        var start = Editor.SnapTime(PlayheadTimeSec);
        var end = Math.Max(SequenceEndSec, start + 1);
        var length = Math.Min(4, end - start);
        var count = Editor.Document.Markers.Count(m => m.IsRange) + 1;
        var marker = Editor.AddRangeMarker(start, length, $"Range {count}", "#5f9cf8");
        Editor.Selection.SelectMarker(marker.Id);
        Notify?.Invoke($"Added \"{marker.Name}\" \u2014 drag its edges on the timeline to adjust");
    }

    [RelayCommand]
    private void SplitVideoAtSelectedMarkers()
    {
        if (Editor.Selection.SelectedMarkerIds.Count == 0)
        {
            Notify?.Invoke("Select one or more markers first");
            return;
        }
        int count = Editor.SplitVideoAtSelectedMarkers();
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke(count == 0 ? "No video clips cross the selected marker positions"
            : $"Made {count} video cut{(count == 1 ? "" : "s")} at selected markers");
    }

    [RelayCommand]
    private void DeleteSelectedMarker()
    {
        int count = Editor.DeleteMarkers(Editor.Selection.SelectedMarkerIds);
        if (count == 0)
            return;
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke($"Deleted {count} marker{(count == 1 ? "" : "s")}");
    }

    [RelayCommand]
    private void ToggleClipEnabledSelected()
    {
        if (Editor.Selection.Count == 0)
        {
            Notify?.Invoke("Select a clip to enable or disable");
            return;
        }
        Editor.ToggleEnabledSelected();
        Properties.Refresh();
        Preview.RefreshFrame();
    }

    [RelayCommand]
    private void RemoveSelectedTransition()
    {
        if (Editor.Selection.SelectedTransitionKey is null)
        {
            Notify?.Invoke("Select a transition to remove it");
            return;
        }
        Editor.RemoveSelectedTransition();
        Properties.Refresh();
        Preview.RefreshFrame();
        Notify?.Invoke("Removed transition");
    }

    /// <summary>
    /// Opens the export dialog (resolution, fps, quality, output path), then queues the export
    /// as a background job visible in the jobs popup.
    /// </summary>
    [RelayCommand]
    private Task ExportAsync(Window? owner) => RunExportAsync(owner, chapterMode: false);

    /// <summary>Exports each marked range of the timeline as its own video.</summary>
    [RelayCommand]
    private Task ExportMarkedRangesAsync(Window? owner) => RunExportAsync(owner, chapterMode: true);

    private async Task RunExportAsync(Window? owner, bool chapterMode)
    {
        if (Project is null || Editor is null)
            return;
        owner ??= MainWindow;
        if (owner is null)
            return;

        var timeline = Editor.Document;
        var settings = Project.Export;
        var dialog = new ExportDialog(timeline.Rate.Fps, settings.Width, settings.Height, timeline, Project.Name, chapterMode,
            Preferences.Current.Export);
        dialog.Model.CreateRangesFromBookmarks = () => CreateRangesFromBookmarksAsync(dialog);
        var options = await dialog.ShowDialog<ExportOptions?>(owner);
        if (options is null)
            return;

        var width = Math.Max(2, options.Width & ~1);
        var height = Math.Max(2, options.Height & ~1);
        var crf = Math.Clamp(options.Crf, 0, 51);

        if (options.Chapters is { Count: > 0 } chapters)
        {
            System.IO.Directory.CreateDirectory(options.OutputPath);
            foreach (var chapter in chapters)
                Exports.Enqueue(System.IO.Path.Combine(options.OutputPath, chapter.FileName), width, height, crf,
                    Project, timeline, chapter.StartSec, chapter.EndSec);
            IsJobsOpen = true;
            Notify?.Invoke($"Exporting {chapters.Count} video{(chapters.Count == 1 ? "" : "s")} — you can keep editing");
            return;
        }

        Exports.Enqueue(options.OutputPath, width, height, crf, Project, timeline);
        IsJobsOpen = true;
        Notify?.Invoke($"Export started: {System.IO.Path.GetFileName(options.OutputPath)} — you can keep editing");
    }

    [RelayCommand]
    private void ToggleJobs() => IsJobsOpen = !IsJobsOpen;

    /// <summary>
    /// Captures every clip start, runs <paramref name="mutate"/>, then emits slide deltas
    /// for survivors whose start changed (matches RippleDeleteCommand's following set).
    /// </summary>
    private void RunWithRippleSlide(Action mutate)
    {
        var before = SnapshotClipStarts();
        mutate();
        EmitRippleSlides(before);
    }

    private Dictionary<string, double> SnapshotClipStarts()
    {
        var map = new Dictionary<string, double>();
        foreach (var track in Editor.Document.Tracks)
        {
            foreach (var clip in track.Clips)
                map[clip.Id] = clip.StartSec;
        }
        return map;
    }

    private void EmitRippleSlides(Dictionary<string, double> before)
    {
        var deltas = new List<RippleSlideDelta>();
        foreach (var track in Editor.Document.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (!before.TryGetValue(clip.Id, out var oldStart))
                    continue;
                var offset = oldStart - clip.StartSec;
                if (Math.Abs(offset) < 1e-9)
                    continue;
                deltas.Add(new RippleSlideDelta(clip.Id, offset));
            }
        }

        if (deltas.Count > 0)
            RippleSlideStarted?.Invoke(deltas);
    }

    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        if (Playback is null)
            return;
        _stopAtSec = null;
        StopShuttle();
        if (Playback.IsPlaying)
        {
            await Playback.PauseAsync();
        }
        else
        {
            // Flag as playing first so the decode worker fills the playback FIFO, then
            // pre-roll the first video frame before the audio clock starts. This removes
            // the initial black/stale preview stall while playback seeks + decodes the
            // first frame mid-clip.
            IsPlaying = true;
            Preview.PreRollPlayback();
            await Playback.PlayAsync();
        }
        IsPlaying = Playback.IsPlaying;
    }

    [RelayCommand]
    private async Task JumpToStartAsync()
    {
        if (Playback is { IsPlaying: true })
            await Playback.PauseAsync();
        if (Playback is not null)
            await Playback.SeekAsync(0);
        PlayheadTimeSec = 0;
        Preview.PlayheadSec = 0;
        IsPlaying = false;
    }

    [RelayCommand]
    private async Task JumpToEndAsync()
    {
        if (Playback is { IsPlaying: true })
            await Playback.PauseAsync();
        if (Playback is not null)
            await Playback.SeekAsync(SequenceEndSec);
        PlayheadTimeSec = SequenceEndSec;
        Preview.PlayheadSec = SequenceEndSec;
        IsPlaying = false;
    }

    [RelayCommand]
    private void StepBackFrame() => SeekFromUser(Math.Max(0, PlayheadTimeSec - FrameDurationSec));

    [RelayCommand]
    private void StepForwardFrame() => SeekFromUser(PlayheadTimeSec + FrameDurationSec);

    [RelayCommand]
    private async Task GenerateProxyAsync(MediaAsset? asset)
    {
        if (asset is null || ProjectManager is null || asset.Kind != MediaKind.Video)
            return;
        var force = asset.ProxyStatus is ProxyStatus.Ready or ProxyStatus.Failed;
        try
        {
            await System.Threading.Tasks.Task.Run(() => ProjectManager.RequestProxy(asset, force));
        }
        catch
        {
            return;
        }
        Preview.InvalidateSources();
        NotifyMediaArtifactsChanged();
    }

    /// <summary>Puts a media item on the timeline at the playhead (on a free track, or new ones).</summary>
    [RelayCommand]
    private void AddMediaToTimeline(MediaAsset? asset)
    {
        if (asset is null || Editor is null)
            return;
        var start = Editor.SnapTime(PlayheadTimeSec);
        var kind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;
        var free = Editor.Document.Tracks.FirstOrDefault(t => t.Kind == kind
            && !Editor.WouldOverlapGroup(t.Id, Array.Empty<string>(), start, Math.Max(0.1, asset.DurationSec)));
        var clip = free is not null
            ? Editor.AddMediaLinked(asset, free.Id, start)
            : Editor.AddMediaNewTracks(asset, start);
        if (clip is not null)
            Editor.Selection.SelectClips(Editor.LinkGroup(clip.Id).Select(c => c.Id));
        Notify?.Invoke($"Added {asset.FileName} at the playhead");
    }

    [RelayCommand]
    private void RevealMediaInFolder(MediaAsset? asset)
    {
        if (asset is null || string.IsNullOrEmpty(asset.Url))
            return;
        var folder = System.IO.Path.GetDirectoryName(asset.Url);
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
        {
            Notify?.Invoke("The original file's folder can't be found");
            return;
        }
        try
        {
            var launcher = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher, $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Couldn't open the folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveMedia(MediaAsset? asset)
    {
        if (asset is null)
            return;
        Media.Remove(asset);
    }

    private TimelineEditor CreateSeededEditor()
    {
        var timeline = new TimelineModel
        {
            Rate = FrameRate.Common(30),
        };
        var editor = new TimelineEditor(timeline, Preferences.Current.Editing.UndoSteps);
        editor.EnsureTrack(TrackKind.Video);
        editor.EnsureTrack(TrackKind.Audio);
        return editor;
    }
}
