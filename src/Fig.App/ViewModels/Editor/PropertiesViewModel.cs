using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The inspector. Whatever is selected (clips, effect bars, markers, transitions, tracks, library media)
/// is an <see cref="ISelectable"/> that says which facets it has; the sections shown are the facets every
/// selected item shares, and each section's controls write through TimelineEditor.
/// </summary>
public partial class PropertiesViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;
    private bool _suppressApply;
    private string? _clipId;
    // The items currently shown, as one string. While it is unchanged a Refresh() only re-reads values:
    // slider drags raise TimelineChanged on every tick, and tearing the panel down would drop the open tab.
    private string? _contextKey;
    private Facet _facets;

    public PropertiesViewModel(EditorViewModel editor)
    {
        Transform = new TransformControlsViewModel(this, editor);
        Swirl = new SwirlControlsViewModel(this, editor);
        Gizmo = new LayerGizmoViewModel(this, editor);
        _editor = editor;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        Effects.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEffects));
            OnPropertyChanged(nameof(NoEffects));
        };
    }

    public TransformControlsViewModel Transform { get; }
    public SwirlControlsViewModel Swirl { get; }
    /// <summary>The move / scale / rotate gizmo for text and image layers on the preview.</summary>
    public LayerGizmoViewModel Gizmo { get; }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // keyframed param values and automation diamonds follow the playhead
        if (e.PropertyName == nameof(EditorViewModel.PlayheadTimeSec))
        {
            if (Has(Facet.Effects))
                RefreshParams();
            RefreshProperties();
            Transform.Refresh();
            Gizmo.Refresh();
            NotifyPreviewControlsChanged();
        }
    }

    /// <summary>Re-reads every keyframable value (and its keyframe controls) at the playhead, after a keyframe edit.</summary>
    public void RefreshAutomation()
    {
        RefreshProperties();
        RefreshParams();
    }

    /// <summary>The tabs for what is selected, in order. Empty when nothing can be inspected.</summary>
    public ObservableCollection<InspectorSection> Sections { get; } = new();

    [ObservableProperty]
    private InspectorSection? _selectedSection;

    [ObservableProperty] private string _title = "Properties";
    [ObservableProperty] private string _subtitle = "Select a clip, track, or media item.";

    public bool HasContext => Sections.Count > 0;

    /// <summary>A single section needs no tab strip.</summary>
    public bool ShowTabStrip => Sections.Count > 1;

    /// <summary>True when what is shown has this facet (any of the flags given).</summary>
    public bool Has(Facet facet) => (_facets & facet) != 0;

    /// <summary>Opens a section by its facet, if the current selection has one (a rack row jumping to "Audio").</summary>
    public void OpenSection(Facet facet)
    {
        if (Sections.FirstOrDefault(s => s.Facet == facet) is { } section)
            SelectedSection = section;
    }

    /// <summary>Whether the section for this facet is the one open.</summary>
    public bool IsOpen(Facet facet) => SelectedSection?.Facet == facet;

    // shared / media
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private string? _kindLabel;
    [ObservableProperty] private string? _resolutionLabel;
    [ObservableProperty] private string? _durationLabel;
    [ObservableProperty] private string? _proxyStatusLabel;
    [ObservableProperty] private string? _proxyDetail;
    [ObservableProperty] private bool _showProxySection;
    [ObservableProperty] private bool _canGenerateProxy;
    [ObservableProperty] private bool _isProxyBusy;
    [ObservableProperty] private string _proxyActionLabel = "Generate Proxy";

    // clip controls
    [ObservableProperty] private string? _clipTimingLabel;
    [ObservableProperty] private string? _sourceRangeLabel;

    /// <summary>The "Show controls" switch for handles drawn on the preview (zoom frame, swirl center…).</summary>
    [ObservableProperty]
    private bool _showPreviewControls = true;

    /// <summary>Something selected has handles that can be shown on the preview.</summary>
    public bool HasPreviewControls => Transform.IsActive || Swirl.IsActive || Gizmo.Items.Count > 0;

    partial void OnShowPreviewControlsChanged(bool value)
    {
        Transform.NotifyControlsVisibilityChanged();
        Swirl.NotifyControlsVisibilityChanged();
        Gizmo.NotifyControlsVisibilityChanged();
    }

    internal void NotifyPreviewControlsChanged() => OnPropertyChanged(nameof(HasPreviewControls));


    /// <summary>Source length of the clip before retiming, e.g. "00:12".</summary>
    [ObservableProperty] private string? _originalDurationLabel;
    /// <summary>Length on the timeline after the speed change.</summary>
    [ObservableProperty] private string? _retimedDurationLabel;
    /// <summary>"Linked to Robot_02.mp4" when the clip moves together with a partner clip.</summary>
    [ObservableProperty] private string? _linkedToLabel;
    /// <summary>Clip length in seconds, used to draw the fade envelope.</summary>
    [ObservableProperty] private double _clipDurationSec = 1;
    public bool IsLinked => LinkedToLabel is not null;

    /// <summary>The clip's fades, for the fade envelope drawing (they are edited through the Fades group).</summary>
    [ObservableProperty] private double _fadeInValue;
    [ObservableProperty] private double _fadeOutValue;
    /// <summary>The clip takes its speed from a curve drawn on it (the Speed slider is off).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddSpeedCurve))] private bool _hasSpeedCurve;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddSpeedCurve))] private bool _canHaveSpeedCurve;
    public bool CanAddSpeedCurve => CanHaveSpeedCurve && !HasSpeedCurve;
    [ObservableProperty] private double _speedValue = 1;

    // track
    [ObservableProperty] private string? _trackNameLabel;
    [ObservableProperty] private string? _trackFlagsLabel;

    // marker
    [ObservableProperty] private string _markerName = "";
    [ObservableProperty] private string? _markerTimeLabel;
    [ObservableProperty] private string? _markerDurationLabel;
    [ObservableProperty] private string _markerColor = "#ffd60a";
    [ObservableProperty] private bool _markerIsRange;
    [ObservableProperty] private string _markerStartText = "";
    [ObservableProperty] private string _markerEndText = "";
    [ObservableProperty] private string _markerPositionText = "";
    [ObservableProperty] private string _markerDurationText = "";
    [ObservableProperty] private bool _markerIncludeInExport = true;
    [ObservableProperty] private string _markerOutputName = "";
    [ObservableProperty] private string _markerAttachedText = "Timeline";
    [ObservableProperty] private string _markerAttachedNote = "";
    [ObservableProperty] private string? _markerEffectLabel;
    public bool MarkerHasEffect => MarkerEffectLabel is not null;
    public string MarkerTitle => MarkerIsRange ? "Range marker" : "Point marker";
    private FrameRate _markerRate;

    // transition
    [ObservableProperty] private string? _transitionTypeLabel;
    [ObservableProperty] private string? _transitionDescription;
    [ObservableProperty] private string? _transitionEditPoint;
    [ObservableProperty] private string? _transitionOutgoing;
    [ObservableProperty] private string? _transitionIncoming;
    [ObservableProperty] private string _transitionDurationText = "";
    [ObservableProperty] private string? _transitionSpanLabel;
    [ObservableProperty] private double _transitionDurationValue;
    [ObservableProperty] private double _transitionMaxDuration = 1;

    // effect stack (clip context)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGenericParams))]
    [NotifyPropertyChangedFor(nameof(IsSelectedEffectCustom))]
    private EffectItemViewModel? _selectedEffect;
    public ObservableCollection<EffectItemViewModel> Effects { get; } = new();
    public ObservableCollection<ParamEditorViewModel> EffectParams { get; } = new();
    public ObservableCollection<ParamEditorViewModel> TransitionParams { get; } = new();

    public bool HasEffects => Effects.Count > 0;

    /// <summary>The generic parameter list is hidden for effects that have their own panel.</summary>
    public bool ShowGenericParams => SelectedEffect is not null && !Transform.IsActive;

    /// <summary>True when the selected effect is one the user built (so it can be opened in the graph editor).</summary>
    public bool IsSelectedEffectCustom
        => SelectedEffect is { } effect && EffectCatalog.Find(effect.TypeId)?.IsCustom == true;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void EditSelectedGraph()
    {
        if (SelectedEffect is { } effect)
            _editor.EditGraphEffect(effect.TypeId);
    }
    public bool NoEffects => Effects.Count == 0;

    private string? _lastEffectsClipId;
    private string _lastEffectsSignature = "";
    private string _lastTransitionSignature = "";
    private string? _selectedEffectId;
    private string? _lastBuiltEffectId;

    /// <summary>Color swatches offered in the marker inspector (blue, teal, green, amber, red).</summary>
    public static readonly string[] MarkerColors = ["#5f9cf8", "#5ba3b0", "#7fc8a0", "#eab308", "#ff6b6b"];

    public ObservableCollection<MarkerSwatchViewModel> MarkerSwatches { get; } = new();

    private string? _markerId;
    private string? _transitionKey;
    private MediaAsset? _proxyTarget;

    public void Refresh()
    {
        RefreshCore();
        Transform.Refresh();
        Swirl.Refresh();
        Gizmo.Refresh();
        NotifyPreviewControlsChanged();
    }

    private void RefreshCore()
    {
        var ed = _editor.Editor;
        IReadOnlyList<ISelectable> items = ed.Selected;
        if (items.Count == 0 && _editor.SelectedMedia is { } media)
            items = new ISelectable[] { media };   // nothing on the timeline: the library's pick is what is inspected

        var shared = items.Count == 0
            ? Facet.None
            : items.Select(item => item.Facets).Aggregate((a, b) => a & b) & InspectorSections.Supported;
        if (shared == Facet.None)
        {
            ShowEmpty();
            if (items.Count > 1)
            {
                // a mixed selection (clips and markers, say) has nothing every item can be edited as
                Title = $"{items.Count} selected";
                Subtitle = "Nothing to edit that they all share";
            }
            return;
        }

        _suppressApply = true;
        try
        {
            var identity = string.Join("|", items.Select(item => $"{item.Key.Group}:{item.Key.Id}"));
            var sameContext = identity == _contextKey;
            if (!sameContext)
            {
                ResetState();
                _contextKey = identity;
            }
            _facets = shared;
            SyncSections(shared, resetTab: !sameContext);

            var primary = items[^1];
            var owner = primary.EffectOwner();
            _clipId = owner?.Id;
            Title = items.Count == 1 ? primary.DisplayName : $"{items.Count} selected";
            Subtitle = items.Count == 1 ? "" : string.Join(", ", items.Select(item => item.DisplayName).Distinct());

            // each facet loads its own values; none of them asks what kind of thing the item is
            var clip = items.OfType<Clip>().LastOrDefault();
            LoadClip(clip, primary, items.Count == 1);
            LoadProperties(items.OfType<Clip>().ToList());
            if (Has(Facet.Track))
                LoadTrack(primary.Key.Id, items.Count == 1);
            if (Has(Facet.Marker))
                LoadMarker(primary.Key.Id, sameContext);
            if (Has(Facet.Transition))
                LoadTransition(primary.Key.Id);
            if (Has(Facet.Effects) && owner is not null)
                LoadEffects(owner, items.Count == 1 && primary is EffectTarget);
            if (primary is EffectTarget target && items.Count == 1)
                Subtitle = OwnerLabel(target.Owner);
        }
        finally
        {
            _suppressApply = false;
        }
    }

    /// <summary>Tabs follow the shared facets. The same facets keep their sections (and the open tab); new ones open on the first.</summary>
    private void SyncSections(Facet shared, bool resetTab)
    {
        var wanted = InspectorSections.For(shared, this).ToList();
        if (Sections.Select(s => s.Facet).SequenceEqual(wanted.Select(s => s.Facet)))
        {
            if (resetTab)
                SelectedSection = Sections.FirstOrDefault();
            return;
        }

        var previous = SelectedSection?.Facet;
        Sections.Clear();
        foreach (var section in wanted)
            Sections.Add(section);
        SelectedSection = (!resetTab ? Sections.FirstOrDefault(s => s.Facet == previous) : null) ?? Sections.FirstOrDefault();
        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(ShowTabStrip));
    }

    private void ShowEmpty()
    {
        _suppressApply = true;
        ResetState();
        ClearProperties();
        _contextKey = null;
        _facets = Facet.None;
        Sections.Clear();
        SelectedSection = null;
        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(ShowTabStrip));
        Title = "Inspector";
        Subtitle = "No selection";
        _clipId = null;
        _proxyTarget = null;
        ShowProxySection = false;
        CanGenerateProxy = false;
        IsProxyBusy = false;
        _suppressApply = false;
    }

    /// <summary>The label a track or clip goes by ("V1", a file name).</summary>
    private string OwnerLabel(IEffectOwner owner)
        => owner is Track track ? TrackLabel(track) : ClipFileName((Clip)owner);

    private static string TrackLabel(Track track)
        => string.IsNullOrWhiteSpace(track.Name)
            ? (track.Kind == TrackKind.Video ? $"V{track.Index + 1}" : $"A{track.Index + 1}")
            : track.Name!;

    /// <summary>Loads the values of the clip tabs (picture, sound, fades, speed) and the file behind the clip or library item.</summary>
    private void LoadClip(Clip? clip, ISelectable primary, bool single)
    {
        MediaAsset? asset = null;
        if (clip is not null)
        {
            ClipTimingLabel = $"{FormatTime(clip.StartSec)} → {FormatTime(clip.StartSec + clip.DurSec)}  ({FormatTime(clip.DurSec)})";
            FadeInValue = clip.FadeInSec;
            FadeOutValue = clip.FadeOutSec;
            SpeedValue = clip.Speed;
            HasSpeedCurve = ClipTime.HasCurve(clip);
            CanHaveSpeedCurve = _editor.Editor.CanHaveSpeedCurve(clip.Id);
            ClipDurationSec = Math.Max(0.01, clip.DurSec);
            RetimedDurationLabel = FormatTime(clip.DurSec);
            OriginalDurationLabel = FormatTime(ClipTime.SourceOffset(clip, clip.DurSec));
            if (clip.MediaId is { } mediaId)
                _editor.MediaById.TryGetValue(mediaId, out asset);
            if (Has(Facet.Source) && clip.HasSourceRange)
                SourceRangeLabel = $"{FormatTime(clip.SourceIn)} → {FormatTime(clip.SourceOut > 0 ? clip.SourceOut : ClipTime.SourceAt(clip, clip.DurSec))}";
            else
                SourceRangeLabel = null;
            LinkedToLabel = clip.LinkGroupId is not null && asset is not null ? $"Linked to {asset.FileName}" : null;
            OnPropertyChanged(nameof(IsLinked));
            LoadLayer(clip);
            if (single)
                Subtitle = asset?.FileName ?? ClipTimingLabel;
        }
        else if (primary is MediaAsset library)
        {
            asset = library;
            if (single)
                Subtitle = library.FileName;
        }

        if (asset is not null && Has(Facet.Source))
        {
            ApplyMediaFields(asset);
            ShowProxySection = asset.Kind == MediaKind.Video;
            _proxyTarget = asset.Kind == MediaKind.Video ? asset : null;
            UpdateProxyAction(asset);
        }
        else
        {
            _proxyTarget = null;
            ShowProxySection = false;
            CanGenerateProxy = false;
        }
    }

    private void LoadTrack(string trackId, bool single)
    {
        var track = _editor.Editor.Document.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
            return;
        var label = TrackLabel(track);
        if (single)
            Subtitle = label;
        TrackNameLabel = label;
        KindLabel = track.Kind == TrackKind.Video ? "Video" : "Audio";
        var flags = new List<string>();
        if (track.Kind == TrackKind.Video)
            flags.Add(track.Visible ? "Visible" : "Hidden");
        if (track.Kind == TrackKind.Audio)
            flags.Add(track.Muted ? "Muted" : "Audible");
        TrackFlagsLabel = string.Join(" · ", flags);
    }

    /// <summary>Turns every effect on the clip off, or back on when they are all already off.</summary>
    [RelayCommand]
    private void BypassAllEffects()
    {
        if (_clipId is null || _editor.Editor.FindEffectOwner(_clipId) is not { } clip || clip.Effects.Count == 0)
            return;
        var turnOff = clip.Effects.Any(e => e.Enabled);
        foreach (var effect in clip.Effects.Where(e => e.Enabled == turnOff).ToList())
            _editor.Editor.ToggleEffect(clip.Id, effect.Id);
        _lastEffectsClipId = null;
        Refresh();
        _editor.Preview.RefreshFrame();
    }

    [RelayCommand]
    private void ResetSpeed() => SpeedValue = 1;

    [RelayCommand]
    private void AddSpeedCurve()
    {
        if (_clipId is not null)
            _editor.Editor.AddSpeedCurve(_clipId);
    }

    [RelayCommand]
    private void RemoveSpeedCurve()
    {
        if (_clipId is not null)
            _editor.Editor.RemoveSpeedCurve(_clipId);
    }

    private void LoadMarker(string markerId, bool sameContext)
    {
        if (_editor.Editor.FindMarker(markerId) is not { } location)
            return;
        var marker = location.Marker;
        var absolute = location.Clip is not null ? location.Clip.StartSec + marker.StartSec : marker.StartSec;
        var rate = _editor.Editor.Document.Rate;

        _clipId = null;
        _markerId = marker.Id;
        _markerRate = rate;
        MarkerIsRange = marker.IsRange;
        OnPropertyChanged(nameof(MarkerTitle));
        MarkerName = marker.Name;
        MarkerColor = marker.Color;
        RefreshSwatches(marker.Color);
        MarkerTimeLabel = FormatTime(absolute);
        MarkerDurationLabel = marker.DurSec > 0 ? FormatTime(marker.DurSec) : "Point";
        MarkerPositionText = Timecode.Format(absolute, rate);
        MarkerStartText = Timecode.Format(marker.StartSec, rate);
        MarkerEndText = Timecode.Format(marker.EndSec, rate);
        MarkerDurationText = $"Duration \u00b7 {marker.DurSec:0.00} seconds";
        MarkerIncludeInExport = marker.IncludeInExport;
        MarkerOutputName = marker.OutputName ?? "";
        if (location.Clip is not null)
        {
            MarkerAttachedText = "Clip";
            MarkerAttachedNote = "Moves with its clip.";
        }
        else if (location.Track is not null)
        {
            MarkerAttachedText = "Track";
            MarkerAttachedNote = "Stays on this track.";
        }
        else
        {
            MarkerAttachedText = "Timeline";
            MarkerAttachedNote = "Stays at this sequence time.";
        }
        MarkerEffectLabel = SelectedEffect is { } effect ? $"Selected effect: {effect.DisplayName}" : null;
        OnPropertyChanged(nameof(MarkerHasEffect));
    }

    private void RefreshSwatches(string selected)
    {
        if (MarkerSwatches.Count != MarkerColors.Length)
        {
            MarkerSwatches.Clear();
            foreach (var hex in MarkerColors)
                MarkerSwatches.Add(new MarkerSwatchViewModel(hex, SetMarkerColor));
        }
        foreach (var swatch in MarkerSwatches)
            swatch.IsSelected = string.Equals(swatch.Hex, selected, StringComparison.OrdinalIgnoreCase);
    }

    private double MarkerBaseSec()
        => _markerId is not null && _editor.Editor.FindMarker(_markerId) is { Clip: { } clip } ? clip.StartSec : 0;

    private void EditMarker(MarkerEdit edit)
    {
        if (_markerId is null || !Has(Facet.Marker))
            return;
        _editor.Editor.EditMarker(_markerId, edit);
        Refresh();
    }

    /// <summary>Re-reads the marker after a typed value was rejected, so the box shows the real value.</summary>
    private void RevertMarkerText() => Refresh();

    partial void OnMarkerPositionTextChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Marker))
            return;
        if (!Timecode.TryParse(value, _markerRate, out var sec)) { RevertMarkerText(); return; }
        EditMarker(new MarkerEdit(StartSec: sec - MarkerBaseSec()));
    }

    partial void OnMarkerStartTextChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Marker))
            return;
        if (!Timecode.TryParse(value, _markerRate, out var start)
            || _markerId is null || _editor.Editor.FindMarker(_markerId) is not { } loc) { RevertMarkerText(); return; }
        // moving the start keeps the end where it is
        var end = loc.Marker.EndSec;
        if (start >= end - 1e-3) { RevertMarkerText(); return; }
        EditMarker(new MarkerEdit(StartSec: start, DurSec: end - start));
    }

    partial void OnMarkerEndTextChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Marker))
            return;
        if (!Timecode.TryParse(value, _markerRate, out var end)
            || _markerId is null || _editor.Editor.FindMarker(_markerId) is not { } loc) { RevertMarkerText(); return; }
        if (end <= loc.Marker.StartSec + 1e-3) { RevertMarkerText(); return; }
        EditMarker(new MarkerEdit(DurSec: end - loc.Marker.StartSec));
    }

    partial void OnMarkerIncludeInExportChanged(bool value)
    {
        if (_suppressApply || !Has(Facet.Marker))
            return;
        EditMarker(new MarkerEdit(IncludeInExport: value));
    }

    partial void OnMarkerOutputNameChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Marker))
            return;
        EditMarker(new MarkerEdit(OutputName: value.Trim()));
    }

    [RelayCommand]
    private void JumpToMarker()
    {
        if (_markerId is null || _editor.Editor.FindMarker(_markerId) is not { } loc)
            return;
        _editor.SeekFromUser(loc.Marker.StartSec + MarkerBaseSec());
    }

    [RelayCommand]
    private void JumpToMarkerEnd()
    {
        if (_markerId is null || _editor.Editor.FindMarker(_markerId) is not { } loc)
            return;
        _editor.SeekFromUser(loc.Marker.EndSec + MarkerBaseSec());
    }

    /// <summary>Moves the playhead to the marker and keyframes every parameter of the selected effect there.</summary>
    [RelayCommand]
    private void PlaceKeyframeAtMarker()
    {
        if (SelectedEffect is null)
            return;
        JumpToMarker();
        foreach (var param in EffectParams.ToList())
            if (!param.Target.IsOnKeyframe)
                param.ToggleKeyframeCommand.Execute(null);
    }

    [RelayCommand]
    private void DeleteMarker()
    {
        if (_markerId is null)
            return;
        _editor.Editor.DeleteMarker(_markerId);
        Refresh();
    }

    partial void OnMarkerNameChanged(string value)
    {
        if (_suppressApply || _markerId is null || !Has(Facet.Marker))
            return;
        _editor.Editor.UpdateMarker(_markerId, name: value);
        }

    [RelayCommand]
    private void SetMarkerColor(string? hex)
    {
        if (_markerId is null || !Has(Facet.Marker) || string.IsNullOrEmpty(hex))
            return;
        _editor.Editor.UpdateMarker(_markerId, color: hex);
        _suppressApply = true;
        MarkerColor = hex;
        _suppressApply = false;
    }

    private void LoadTransition(string key)
    {
        if (_editor.Editor.GetTransition(key) is not { } transition)
            return;
        var displayName = TransitionCatalog.Find(transition.TypeId)?.DisplayName ?? transition.TypeId;

        _clipId = null;
        _transitionKey = transition.Key;
        TransitionTypeLabel = displayName;
        TransitionDescription = TransitionCatalog.Find(transition.TypeId)?.Description;
        TransitionEditPoint = Timecode.Format(transition.CutSec, _editor.Editor.Document.Rate);
        TransitionOutgoing = ClipFileName(transition.Left);
        TransitionIncoming = ClipFileName(transition.Right);
        Subtitle = $"{TransitionOutgoing} \u2192 {TransitionIncoming}";
        TransitionDurationValue = transition.DurationSec;
        TransitionDurationText = $"{transition.DurationSec:0.00} s";
        TransitionMaxDuration = Math.Max(transition.DurationSec, Math.Max(0.05,
            _editor.Editor.GetMaxTransitionDuration(transition.LeftClipId, transition.RightClipId)));
        TransitionSpanLabel = $"{FormatTime(transition.DurationSec)} across the cut";
        BuildTransitionParams(transition);
    }

    partial void OnTransitionDurationValueChanged(double value)
    {
        if (_suppressApply || _transitionKey is null || !Has(Facet.Transition))
            return;
        var transition = _editor.Editor.GetTransition(_transitionKey);
        if (transition is null)
            return;
        var clamped = Math.Clamp(value, 0, TransitionMaxDuration);
        _editor.Editor.SetTransitionDuration(transition.LeftClipId, transition.RightClipId, clamped);
        if (Math.Abs(clamped - value) > 1e-6)
        {
            _suppressApply = true;
            TransitionDurationValue = clamped;
            _suppressApply = false;
        }
        TransitionSpanLabel = $"{FormatTime(clamped)} across the cut";
        TransitionDurationText = $"{clamped:0.00} s";
        _editor.Preview.RefreshFrame();
    }

    /// <summary>Applies a typed duration such as "1.4" or "1.40 s".</summary>
    partial void OnTransitionDurationTextChanged(string value)
    {
        if (_suppressApply || !Has(Facet.Transition))
            return;
        var text = value.Replace("s", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0 && Math.Abs(seconds - TransitionDurationValue) > 1e-6)
            TransitionDurationValue = seconds;
        else
            TransitionDurationText = $"{TransitionDurationValue:0.00} s";
    }

    private string ClipFileName(Clip clip)
        => clip switch
        {
            VideoClip v when _editor.MediaById.TryGetValue(v.SourceId, out var a) => a.FileName,
            AudioClip au when _editor.MediaById.TryGetValue(au.SourceId, out var a) => a.FileName,
            _ => "clip",
        };

    internal CutTransition? CurrentTransition()
        => Has(Facet.Transition) && _transitionKey is not null
            ? _editor.Editor.GetTransition(_transitionKey)
            : null;

    internal void WriteTransitionParam(string key, ParamValue value)
    {
        if (CurrentTransition() is not { } t)
            return;
        _editor.Editor.SetTransitionParam(t.LeftClipId, t.RightClipId, key, value);
        _editor.Preview.RefreshFrame();
    }

    /// <summary>Plays across the cut so the transition can be judged.</summary>
    [RelayCommand]
    private void PreviewTransition()
    {
        if (CurrentTransition() is not { } t)
            return;
        var half = t.DurationSec / 2;
        _editor.PlayRange(Math.Max(0, t.CutSec - half - 0.4), t.CutSec + half + 0.4);
    }

    /// <summary>Opens the transitions library so a different one can be dragged onto the cut.</summary>
    [RelayCommand]
    private void ReplaceTransition() => _editor.OpenTransitionsLibrary();

    /// <summary>Puts every control of the transition back to its default.</summary>
    [RelayCommand]
    private void ResetTransitionControls()
    {
        if (CurrentTransition() is not { } t || TransitionCatalog.Find(t.TypeId) is not { } entry)
            return;
        foreach (var def in entry.ParamSchema)
            _editor.Editor.SetTransitionParam(t.LeftClipId, t.RightClipId, def.Key, def.DefaultValue());
        _lastTransitionSignature = "";
        Refresh();
        _editor.Preview.RefreshFrame();
    }

    [RelayCommand]
    private void RemoveTransition()
    {
        if (_transitionKey is null || !Has(Facet.Transition))
            return;
        var transition = _editor.Editor.GetTransition(_transitionKey);
        if (transition is null)
            return;
        _editor.Editor.RemoveTransition(transition.LeftClipId, transition.RightClipId);
        _editor.Preview.RefreshFrame();
        Refresh();
    }

    // ---- effect stack + schema-driven param editors ----

    /// <summary>Shows the owner's effect stack, with the picked effect (when an effect bar was picked) open.</summary>
    private void LoadEffects(IEffectOwner owner, bool focusPicked)
    {
        BuildEffectStack(owner);
        if (focusPicked && _editor.Editor.Selection.SelectedEffect is { } picked)
            SelectedEffect = Effects.FirstOrDefault(item => item.EffectId == picked.EffectId);
    }

    private void BuildEffectStack(IEffectOwner clip)
    {
        // Same clip as last time: only rebuild the list when the stack actually changed
        // (add/remove/reorder/toggle). The signature covers ids + order + enabled state;
        // otherwise just refresh the evaluated values of the existing param editors.
        var signature = EffectSignature(clip);
        if (_lastEffectsClipId == clip.Id && _lastEffectsSignature == signature)
        {
            RefreshParams();
            return;
        }

        _lastEffectsClipId = clip.Id;
        _lastEffectsSignature = signature;
        Effects.Clear();
        foreach (var effect in clip.Effects)
        {
            var entry = EffectCatalog.Find(effect.TypeId);
            Effects.Add(new EffectItemViewModel(
                effect.Id,
                effect.TypeId,
                entry?.DisplayName ?? effect.TypeId,
                entry?.Icon ?? "wand-sparkles",
                effect.Enabled,
                ToggleEffect,
                RemoveEffect));
        }

        SelectedEffect = Effects.FirstOrDefault();
        _selectedEffectId = SelectedEffect?.EffectId;
        RebuildEffectParams();
    }

    private static string EffectSignature(IEffectOwner clip)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var effect in clip.Effects)
        {
            sb.Append(effect.Id).Append(':').Append(effect.Order).Append(':')
              .Append(effect.Enabled ? 1 : 0).Append(';');
        }
        return sb.ToString();
    }

    partial void OnSelectedSectionChanged(InspectorSection? value) => Transform?.Refresh();

    partial void OnSelectedEffectChanged(EffectItemViewModel? value)
    {
        var nextId = value?.EffectId;
        if (nextId == _selectedEffectId && nextId == _lastBuiltEffectId)
            return;
        _selectedEffectId = nextId;
        SelectedKeyframeSec = null;
        RebuildEffectParams();
        Transform?.Refresh();
    }

    private void ToggleEffect(EffectItemViewModel item)
    {
        if (_clipId is null)
            return;
        _editor.Editor.ToggleEffect(_clipId, item.EffectId);
        item.IsEnabled = !item.IsEnabled;
    }

    private void RemoveEffect(EffectItemViewModel item)
    {
        if (_clipId is null)
            return;
        _editor.Editor.RemoveEffect(_clipId, item.EffectId);
        _lastEffectsClipId = null; // force the stack to rebuild on the next refresh
    }

    private void RebuildEffectParams()
    {
        EffectParams.Clear();
        _lastBuiltEffectId = _selectedEffectId;
        if (_clipId is null || _selectedEffectId is null)
            return;
        var owner = _editor.Editor.FindEffectOwner(_clipId);
        var effect = owner is null ? null : FindEffect(owner, _selectedEffectId);
        if (effect is null || EffectCatalog.Find(effect.TypeId) is not { } entry)
            return;

        var (clipId, effectId) = (_clipId, effect.Id);
        foreach (var def in entry.ParamSchema)
        {
            var key = def.Key;
            // an effect's parameter: the same keyed value as a clip's property
            var target = new KeyedParamTarget(_editor, KeyframeHostRef.OfEffect(clipId, effectId), key, canKey: true,
                readBase: () => FindEffectById(clipId, effectId)?.Params.TryGetValue(key, out var v) == true ? v : def.DefaultValue(),
                writeBase: value => _editor.Editor.SetEffectParam(clipId, effectId, key, value));
            EffectParams.Add(ParamEditorViewModel.For(def, target));
        }
        RefreshParams();
    }

    private EffectInstance? FindEffectById(string ownerId, string effectId)
        => _editor.Editor.FindEffectOwner(ownerId) is { } owner ? FindEffect(owner, effectId) : null;

    private void RefreshParams()
    {
        foreach (var editor in EffectParams)
            editor.RefreshValue();
    }

    /// <summary>The clip and effect currently selected in the Effects tab, if any.</summary>
    internal (IEffectOwner Clip, EffectInstance Effect)? SelectedEffectInstance()
    {
        if (_clipId is null || _selectedEffectId is null || _editor.Editor.FindEffectOwner(_clipId) is not { } clip
            || FindEffect(clip, _selectedEffectId) is not { } effect)
            return null;
        return (clip, effect);
    }

    /// <summary>The keyframe (clip-relative seconds) picked on the selected effect's bar, if any.</summary>
    [ObservableProperty]
    private double? _selectedKeyframeSec;

    public void SelectKeyframe(double sec) => SelectedKeyframeSec = sec;

    partial void OnSelectedKeyframeSecChanged(double? value) => Transform?.Refresh();

    internal string? SelectedClipFileName => FileName;

    internal void NotifyGenericParamsChanged() => OnPropertyChanged(nameof(ShowGenericParams));

    internal void WriteSelectedEffectParam(string key, ParamValue value)
    {
        if (SelectedEffectInstance() is { } sel)
            WriteEffectParam(sel.Clip.Id, sel.Effect.Id, key, value);
    }

    private void WriteEffectParam(string clipId, string effectId, string key, ParamValue value)
    {
        var clip = _editor.Editor.FindEffectOwner(clipId);
        if (clip is null)
            return;
        var effect = FindEffect(clip, effectId);
        var keyframed = effect?.Keyframes.TryGetValue(key, out var track) == true && track.Count > 0;
        if (keyframed)
            _editor.Editor.SetKeyframe(clipId, effectId, key, Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec), value);
        else
            _editor.Editor.SetEffectParam(clipId, effectId, key, value);
    }

    private void BuildTransitionParams(CutTransition transition)
    {
        // Only rebuild when the transition structure (type / parameter set) actually changed;
        // slider drags re-enter via TimelineChanged on every tick and would otherwise replace
        // the editor mid-drag.
        var signature = TransitionSignature(transition);
        if (_lastTransitionSignature == signature)
            return;
        _lastTransitionSignature = signature;

        TransitionParams.Clear();
        var entry = TransitionCatalog.Find(transition.TypeId);
        if (entry is null)
            return;

        foreach (var def in entry.ParamSchema)
        {
            var key = def.Key;
            ParamValue Current()
                => transition.Left.TransitionOut?.Params.TryGetValue(key, out var v1) == true ? v1
                    : transition.Right.TransitionIn?.Params.TryGetValue(key, out var v2) == true ? v2
                    : def.DefaultValue();
            var editor = ParamEditorViewModel.For(def, new PlainParamTarget(Current,
                value => _editor.Editor.SetTransitionParam(transition.LeftClipId, transition.RightClipId, key, value)));
            editor.RefreshValue();
            TransitionParams.Add(editor);
        }
    }

    private static string TransitionSignature(CutTransition transition)
    {
        // Structural only (type + which params exist). Param values update through the
        // two-way slider bindings; including them would rebuild the editors mid-drag on
        // every TimelineChanged tick.
        var sb = new System.Text.StringBuilder();
        sb.Append(transition.TypeId).Append('|');
        var source = transition.Left.TransitionOut?.Params
                     ?? transition.Right.TransitionIn?.Params
                     ?? new Dictionary<string, ParamValue>();
        foreach (var key in source.Keys)
            sb.Append(key).Append(';');
        return sb.ToString();
    }

    private static EffectInstance? FindEffect(IEffectOwner clip, string effectId)
    {
        foreach (var effect in clip.Effects)
            if (effect.Id == effectId)
                return effect;
        return null;
    }

    /// <summary>Picks an effect bar (a bar of a clip or a track) and opens the Effects tab on it.</summary>
    public void SelectEffect(string clipId, string effectId)
    {
        var editor = _editor.Editor;
        editor.Selection.SelectEffect(clipId, effectId);
        Refresh();
        foreach (var item in Effects)
            if (item.EffectId == effectId)
            {
                SelectedEffect = item;
                break;
            }
        OpenSection(Facet.Effects);
    }

    private void ApplyMediaFields(MediaAsset asset)
    {
        FileName = asset.FileName;
        KindLabel = asset.Kind.ToString();
        ResolutionLabel = asset.Width > 0 && asset.Height > 0
            ? $"{asset.Width}×{asset.Height}"
            : null;
        DurationLabel = FormatTime(asset.DurationSec);
        ProxyStatusLabel = asset.Kind == MediaKind.Video
            ? ProxyLabel(asset)
            : null;
        ProxyDetail = asset.Kind != MediaKind.Video
            ? null
            : asset.ProxyStatus switch
            {
                ProxyStatus.Ready => "Preview uses the proxy; export still uses the original.",
                ProxyStatus.Pending => "Encoding in background…",
                ProxyStatus.Failed => "Last attempt failed. You can retry.",
                ProxyStatus.None when MediaService.ShouldGenerateProxy(asset.Width, asset.Height)
                    => "No proxy yet. Generate one for smoother scrubbing.",
                _ => "Source is already small — proxy not needed.",
            };
    }

    private void UpdateProxyAction(MediaAsset asset)
    {
        if (asset.Kind != MediaKind.Video)
        {
            CanGenerateProxy = false;
            return;
        }

        var needs = MediaService.ShouldGenerateProxy(asset.Width, asset.Height);
        CanGenerateProxy = needs && !IsProxyBusy;
        ProxyActionLabel = asset.ProxyStatus switch
        {
            ProxyStatus.Ready => "Regenerate Proxy",
            ProxyStatus.Failed => "Retry Proxy",
            ProxyStatus.Pending => "Generating…",
            _ => "Generate Proxy",
        };
    }

    private static string ProxyLabel(MediaAsset asset) => asset.ProxyStatus switch
    {
        ProxyStatus.Ready => "Ready",
        ProxyStatus.Pending => "Pending",
        ProxyStatus.Failed => "Failed",
        _ when MediaService.ShouldGenerateProxy(asset.Width, asset.Height) => "Not generated",
        _ => "Not needed",
    };

    /// <summary>Forgets everything shown, so a different selection starts from a clean panel.</summary>
    private void ResetState()
    {
        FileName = null;
        KindLabel = null;
        ResolutionLabel = null;
        DurationLabel = null;
        ProxyStatusLabel = null;
        ProxyDetail = null;
        ClipTimingLabel = null;
        SourceRangeLabel = null;
        TrackNameLabel = null;
        TrackFlagsLabel = null;
        MarkerName = "";
        MarkerTimeLabel = null;
        MarkerDurationLabel = null;
        TransitionTypeLabel = null;
        TransitionSpanLabel = null;
        Effects.Clear();
        EffectParams.Clear();
        TransitionParams.Clear();
        SelectedEffect = null;
        _selectedEffectId = null;
        _lastEffectsClipId = null;
        _lastEffectsSignature = "";
        _lastTransitionSignature = "";
        _lastBuiltEffectId = null;
        _markerId = null;
        _transitionKey = null;
    }

    partial void OnSpeedValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || !Has(Facet.Retime))
            return;
        _editor.Editor.SetSpeed(_clipId, value);
        _editor.Preview.RefreshFrame();
    }

    private Clip? FindClip(string id)
    {
        foreach (var track in _editor.Editor.Document.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == id);
            if (clip is not null)
                return clip;
        }
        return null;
    }

    private static string ShortId(string id)
        => id.Length <= 8 ? id : id[..8];

    private static string FormatTime(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0)
            sec = 0;
        var t = TimeSpan.FromSeconds(sec);
        if (t.TotalHours >= 1)
            return t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        return t.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task GenerateProxyAsync()
    {
        var asset = _proxyTarget;
        var manager = _editor.ProjectManager;
        if (asset is null || manager is null || IsProxyBusy)
            return;

        var force = asset.ProxyStatus is ProxyStatus.Ready or ProxyStatus.Failed;
        IsProxyBusy = true;
        CanGenerateProxy = false;
        ProxyActionLabel = "Generating…";
        ProxyStatusLabel = "Pending";
        ProxyDetail = "Encoding in background…";
        asset.ProxyStatus = ProxyStatus.Pending;

        try
        {
            await Task.Run(() => manager.RequestProxy(asset, force));
        }
        finally
        {
            IsProxyBusy = false;
            _editor.Preview.InvalidateSources();
            _editor.NotifyMediaArtifactsChanged();
            Refresh();
        }
    }
}
