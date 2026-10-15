using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

public enum PropertiesContextKind
{
    Empty,
    Media,
    Clip,
    Track,
    Marker,
    Transition,
}

/// <summary>
/// Contextual inspector for the timeline-row properties panel.
/// Priority: selected clip(s) → selected track → library media → empty.
/// Clip opacity / volume / crop are live controls that write through TimelineEditor.
/// </summary>
public partial class PropertiesViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;
    private bool _suppressApply;
    private string? _clipId;
    // Context key (kind + object id) currently displayed. Keeps the inspector tab stable
    // across the Refresh() re-runs that slider drags trigger (each write raises
    // TimelineChanged), so editing an effect/opacity doesn't yank back to the Video tab.
    private (PropertiesContextKind Kind, string? Key) _shown;

    public PropertiesViewModel(EditorViewModel editor)
    {
        Transform = new TransformControlsViewModel(this, editor);
        Swirl = new SwirlControlsViewModel(this, editor);
        _editor = editor;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        Effects.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEffects));
            OnPropertyChanged(nameof(NoEffects));
        };
        OpacityAutomation = new AutomationButtonViewModel(this, AutomationKeys.Opacity, () => OpacityValue);
        VolumeAutomation = new AutomationButtonViewModel(this, AutomationKeys.Volume, () => VolumeValue);
        CropTopAutomation = new AutomationButtonViewModel(this, AutomationKeys.CropTop, () => CropTop);
        CropBottomAutomation = new AutomationButtonViewModel(this, AutomationKeys.CropBottom, () => CropBottom);
        CropLeftAutomation = new AutomationButtonViewModel(this, AutomationKeys.CropLeft, () => CropLeft);
        CropRightAutomation = new AutomationButtonViewModel(this, AutomationKeys.CropRight, () => CropRight);
    }

    public TransformControlsViewModel Transform { get; }
    public SwirlControlsViewModel Swirl { get; }
    public AutomationButtonViewModel OpacityAutomation { get; }
    public AutomationButtonViewModel VolumeAutomation { get; }
    public AutomationButtonViewModel CropTopAutomation { get; }
    public AutomationButtonViewModel CropBottomAutomation { get; }
    public AutomationButtonViewModel CropLeftAutomation { get; }
    public AutomationButtonViewModel CropRightAutomation { get; }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // keyframed param values and automation diamonds follow the playhead
        if (e.PropertyName == nameof(EditorViewModel.PlayheadTimeSec))
        {
            if (ShowEffectsSection)
                RefreshParams();
            if (Kind == PropertiesContextKind.Clip)
                RefreshAutomation();
            Transform.Refresh();
        }
    }

    /// <summary>True when the automation track has a keyframe at the current playhead.</summary>
    public bool AutomationAtPlayhead(string key)
    {
        if (_clipId is null)
            return false;
        var clip = FindClip(_clipId);
        if (clip is null)
            return false;
        var localT = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, Math.Max(0, clip.DurSec - 1e-4));
        return ClipAutomation.HasKeyframeAt(clip, key, localT);
    }

    /// <summary>Adds a keyframe at the playhead with the given value, or removes one when present.</summary>
    public void ToggleAutomation(string key, double value)
    {
        if (_clipId is null)
            return;
        var clip = FindClip(_clipId);
        if (clip is null)
            return;
        var localT = Math.Clamp(_editor.PlayheadTimeSec - clip.StartSec, 0, Math.Max(0, clip.DurSec - 1e-4));
        if (ClipAutomation.HasKeyframeAt(clip, key, localT))
            _editor.Editor.RemoveClipKeyframe(_clipId, key, localT);
        else
            _editor.Editor.SetClipKeyframe(_clipId, key, localT, value);
        RefreshAutomation();
        _editor.Preview.RefreshFrame();
    }

    /// <summary>Moves the playhead to the previous or next keyframe of a clip automation track.</summary>
    public void StepAutomation(string key, bool forward)
    {
        if (_clipId is null || FindClip(_clipId) is not { } clip
            || !clip.Keyframes.TryGetValue(key, out var track) || track.Count == 0)
            return;
        var localT = Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec);
        var target = forward
            ? track.Where(k => k.TimeSec > localT + 1e-6).OrderBy(k => k.TimeSec).Cast<KeyframePoint?>().FirstOrDefault()
            : track.Where(k => k.TimeSec < localT - 1e-6).OrderByDescending(k => k.TimeSec).Cast<KeyframePoint?>().FirstOrDefault();
        if (target is { } kf)
            _editor.SeekFromUser(clip.StartSec + kf.TimeSec);
    }

    /// <summary>Re-reads all automation diamonds against the current playhead.</summary>
    public void RefreshAutomation()
    {
        OpacityAutomation.Refresh();
        VolumeAutomation.Refresh();
        CropTopAutomation.Refresh();
        CropBottomAutomation.Refresh();
        CropLeftAutomation.Refresh();
        CropRightAutomation.Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ShowTabStrip))]
    private PropertiesContextKind _kind = PropertiesContextKind.Empty;
    [ObservableProperty] private string _title = "Properties";
    [ObservableProperty] private string _subtitle = "Select a clip, track, or media item.";

    public bool HasContext => Kind != PropertiesContextKind.Empty;

    /// <summary>Only a clip has several tabs; markers, tracks and transitions are a single page.</summary>
    public bool ShowTabStrip => Kind is PropertiesContextKind.Clip or PropertiesContextKind.Media;

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
    [ObservableProperty] private bool _showClipSection;
    [ObservableProperty] private bool _showVideoControls;
    [ObservableProperty] private bool _showAudioControls;
    [ObservableProperty] private bool _showFadeControls;
    [ObservableProperty] private bool _showSpeedControls;
    [ObservableProperty] private string? _clipTimingLabel;
    [ObservableProperty] private string? _sourceRangeLabel;
    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>The "Show controls" switch for handles drawn on the preview (zoom frame, swirl center…).</summary>
    [ObservableProperty]
    private bool _showPreviewControls = true;

    /// <summary>Something selected has handles that can be shown on the preview.</summary>
    public bool HasPreviewControls => Transform.IsActive || Swirl.IsActive;

    partial void OnShowPreviewControlsChanged(bool value)
    {
        Transform.NotifyControlsVisibilityChanged();
        Swirl.NotifyControlsVisibilityChanged();
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

    [ObservableProperty] private double _opacityValue = 1;
    [ObservableProperty] private double _fadeInValue;
    [ObservableProperty] private double _fadeOutValue;
    [ObservableProperty] private double _volumeValue = 1;
    [ObservableProperty] private double _speedValue = 1;
    [ObservableProperty] private double _cropLeft;
    [ObservableProperty] private double _cropTop;
    [ObservableProperty] private double _cropRight;
    [ObservableProperty] private double _cropBottom;

    // track
    [ObservableProperty] private string? _trackNameLabel;
    [ObservableProperty] private string? _trackFlagsLabel;
    [ObservableProperty] private bool _showTrackSection;

    // marker
    [ObservableProperty] private bool _showMarkerSection;
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
    [ObservableProperty] private bool _showTransitionSection;
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
    [ObservableProperty] private bool _showEffectsSection;
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
    }

    private void RefreshCore()
    {
        var ed = _editor.Editor;

        if (ed.Selection.SelectedEffect is { } picked && ed.FindEffectOwner(picked.ClipId) is Track effectTrack)
        {
            ShowTrackEffects(effectTrack);
            SelectedEffect = Effects.FirstOrDefault(item => item.EffectId == picked.EffectId);
            return;
        }

        if (ed.Selection.SelectedMarkerId is { } markerId
            && ed.FindMarker(markerId) is { } location)
        {
            ShowMarker(location);
            return;
        }

        if (ed.Selection.SelectedTransitionKey is { } transitionKey
            && ed.GetTransition(transitionKey) is { } transition)
        {
            ShowTransition(transition);
            return;
        }

        var clipId = ed.Selection.SelectedEffect?.ClipId ?? ed.Selection.SelectedClipIds.FirstOrDefault();
        if (clipId is not null)
        {
            var clip = FindClip(clipId);
            if (clip is not null)
            {
                ShowClip(clip);
                if (ed.Selection.SelectedEffect is { } selected)
                    SelectedEffect = Effects.FirstOrDefault(item => item.EffectId == selected.EffectId);
                return;
            }
        }

        var trackId = ed.Selection.ActiveTrackId;
        if (trackId is not null)
        {
            var track = ed.Document.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is not null)
            {
                ShowTrack(track);
                return;
            }
        }

        if (_editor.SelectedMedia is not null)
        {
            ShowMedia(_editor.SelectedMedia);
            return;
        }

        ShowEmpty();
    }

    private void ShowTrackEffects(Track track)
    {
        if (_clipId != track.Id) ClearSections();
        _clipId = track.Id;
        Kind = PropertiesContextKind.Track;
        Title = $"{track.Name ?? "Track"} effects";
        Subtitle = "Timeline effect regions";
        ShowClipSection = false;
        ShowTrackSection = false;
        ShowEffectsSection = true;
        BuildEffectStack(track);
    }

    private void ShowEmpty()
    {
        _suppressApply = true;
        Kind = PropertiesContextKind.Empty;
        Title = "Inspector";
        Subtitle = "No selection";
        ClearSections();
        _clipId = null;
        _proxyTarget = null;
        ShowProxySection = false;
        CanGenerateProxy = false;
        IsProxyBusy = false;
        _shown = (PropertiesContextKind.Empty, null);
        _suppressApply = false;
    }

    /// <summary>
    /// Resets the inspector tab to the first visible one when the shown object changes, but
    /// preserves the user's tab while dragging sliders — every slider tick raises
    /// TimelineChanged and re-runs Show*, which would otherwise jump back to the Video tab.
    /// </summary>
    private void ResetTabFor(PropertiesContextKind kind, string? key)
    {
        var next = (kind, key);
        if (_shown == next)
            return;
        _shown = next;
        SelectedTabIndex = FirstVisibleTab();
    }

    private void ShowMedia(MediaAsset asset)
    {
        _suppressApply = true;
        Kind = PropertiesContextKind.Media;
        Title = "Media";
        Subtitle = asset.FileName;
        ClearSections();
        _clipId = null;
        ApplyMediaFields(asset);
        ShowProxySection = asset.Kind == MediaKind.Video;
        _proxyTarget = asset.Kind == MediaKind.Video ? asset : null;
        UpdateProxyAction(asset);
        ResetTabFor(PropertiesContextKind.Media, asset.Id);
        _suppressApply = false;
    }

    private void ShowClip(Clip clip)
    {
        // Slider/field edits raise TimelineChanged, which re-runs Show* on every tick. When the
        // same clip is already displayed, skip ClearSections: unconditionally flipping the
        // section visibility flags (and clearing the effect/param collections) tears down and
        // recreates the tab's ScrollViewer, snapping the properties panel back to the top
        // mid-drag. Same-value assignments below raise no change events, so the panel just
        // updates labels/values in place.
        var sameClip = Kind == PropertiesContextKind.Clip && _clipId == clip.Id;
        _suppressApply = true;
        Kind = PropertiesContextKind.Clip;
        Title = clip.Kind switch
        {
            ClipKind.Video => "Video Clip",
            ClipKind.Audio => "Audio Clip",
            ClipKind.Text => "Text Clip",
            _ => "Clip",
        };
        if (!sameClip)
            ClearSections();
        _clipId = clip.Id;
        ShowClipSection = true;
        ShowVideoControls = clip is VideoClip;
        ShowAudioControls = clip is AudioClip || clip is VideoClip; // video often has linked volume
        ShowFadeControls = clip is VideoClip || clip is AudioClip;
        ClipTimingLabel = $"{FormatTime(clip.StartSec)} → {FormatTime(clip.StartSec + clip.DurSec)}  ({FormatTime(clip.DurSec)})";
        Subtitle = ClipTimingLabel;
        BuildEffectStack(clip);
        ShowEffectsSection = true;

        OpacityValue = clip.Opacity;
        FadeInValue = clip.FadeInSec;
        FadeOutValue = clip.FadeOutSec;
        VolumeValue = clip.Volume;
        SpeedValue = clip.Speed;
        ShowSpeedControls = clip is VideoClip || clip is AudioClip;

        MediaAsset? asset = null;
        if (clip is VideoClip vc)
        {
            SourceRangeLabel = $"{FormatTime(vc.SrcInSec)} → {FormatTime(vc.SrcOutSec > 0 ? vc.SrcOutSec : vc.SrcInSec + vc.DurSec * vc.Speed)}";
            CropLeft = vc.CropL;
            CropTop = vc.CropT;
            CropRight = vc.CropR;
            CropBottom = vc.CropB;
            _editor.MediaById.TryGetValue(vc.SourceId, out asset);
        }
        else if (clip is AudioClip ac)
        {
            SourceRangeLabel = $"{FormatTime(ac.SrcInSec)} → {FormatTime(ac.SrcOutSec > 0 ? ac.SrcOutSec : ac.SrcInSec + ac.DurSec * ac.Speed)}";
            CropLeft = CropTop = CropRight = CropBottom = 0;
            _editor.MediaById.TryGetValue(ac.SourceId, out asset);
        }
        else
        {
            SourceRangeLabel = null;
            CropLeft = CropTop = CropRight = CropBottom = 0;
        }

        if (asset is not null)
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
        ClipDurationSec = Math.Max(0.01, clip.DurSec);
        RetimedDurationLabel = FormatTime(clip.DurSec);
        OriginalDurationLabel = FormatTime(clip.DurSec * clip.Speed);
        LinkedToLabel = clip.LinkGroupId is not null && asset is not null ? $"Linked to {asset.FileName}" : null;
        OnPropertyChanged(nameof(IsLinked));
        // the inspector names the file, the timing lives in the Speed tab
        Subtitle = asset?.FileName ?? ClipTimingLabel;
        ResetTabFor(PropertiesContextKind.Clip, clip.Id);
        RefreshAutomation();
        _suppressApply = false;
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
    private void ResetCompositing() => OpacityValue = 1;

    [RelayCommand]
    private void ResetAudio() => VolumeValue = 1;

    [RelayCommand]
    private void ResetSpeed() => SpeedValue = 1;

    [RelayCommand]
    private void ResetFades()
    {
        FadeInValue = 0;
        FadeOutValue = 0;
    }

    private void ShowTrack(Track track)
    {
        _suppressApply = true;
        Kind = PropertiesContextKind.Track;
        Title = "Track";
        var label = string.IsNullOrWhiteSpace(track.Name)
            ? (track.Kind == TrackKind.Video ? $"V{track.Index + 1}" : $"A{track.Index + 1}")
            : track.Name!;
        Subtitle = label;
        ClearSections();
        _clipId = track.Id;
        BuildEffectStack(track);
        ShowEffectsSection = true;
        ShowTrackSection = true;
        TrackNameLabel = label;
        KindLabel = track.Kind == TrackKind.Video ? "Video" : "Audio";
        var flags = new List<string>();
        if (track.Kind == TrackKind.Video)
            flags.Add(track.Visible ? "Visible" : "Hidden");
        if (track.Kind == TrackKind.Audio)
            flags.Add(track.Muted ? "Muted" : "Audible");
        TrackFlagsLabel = string.Join(" · ", flags);
        _proxyTarget = null;
        ShowProxySection = false;
        CanGenerateProxy = false;
        _suppressApply = false;
    }

    private void ShowMarker(MarkerLocation location)
    {
        var marker = location.Marker;
        var absolute = location.Clip is not null ? location.Clip.StartSec + marker.StartSec : marker.StartSec;
        var rate = _editor.Editor.Document.Rate;

        // Same marker already shown: skip the destructive clear so typing a marker name
        // (which raises TimelineChanged on every keystroke) doesn't reset the panel.
        var sameMarker = Kind == PropertiesContextKind.Marker && _markerId == marker.Id;
        _suppressApply = true;
        Kind = PropertiesContextKind.Marker;
        Title = marker.IsRange ? "Range marker" : "Point marker";
        Subtitle = "";
        if (!sameMarker)
            ClearSections();
        _clipId = null;
        _markerId = marker.Id;
        _markerRate = rate;
        ShowMarkerSection = true;
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
        _proxyTarget = null;
        ShowProxySection = false;
        CanGenerateProxy = false;
        ResetTabFor(PropertiesContextKind.Marker, marker.Id);
        _suppressApply = false;
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
        if (_markerId is null || Kind != PropertiesContextKind.Marker)
            return;
        _editor.Editor.EditMarker(_markerId, edit);
        Refresh();
    }

    /// <summary>Re-reads the marker after a typed value was rejected, so the box shows the real value.</summary>
    private void RevertMarkerText() => Refresh();

    partial void OnMarkerPositionTextChanged(string value)
    {
        if (_suppressApply || Kind != PropertiesContextKind.Marker)
            return;
        if (!Timecode.TryParse(value, _markerRate, out var sec)) { RevertMarkerText(); return; }
        EditMarker(new MarkerEdit(StartSec: sec - MarkerBaseSec()));
    }

    partial void OnMarkerStartTextChanged(string value)
    {
        if (_suppressApply || Kind != PropertiesContextKind.Marker)
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
        if (_suppressApply || Kind != PropertiesContextKind.Marker)
            return;
        if (!Timecode.TryParse(value, _markerRate, out var end)
            || _markerId is null || _editor.Editor.FindMarker(_markerId) is not { } loc) { RevertMarkerText(); return; }
        if (end <= loc.Marker.StartSec + 1e-3) { RevertMarkerText(); return; }
        EditMarker(new MarkerEdit(DurSec: end - loc.Marker.StartSec));
    }

    partial void OnMarkerIncludeInExportChanged(bool value)
    {
        if (_suppressApply || Kind != PropertiesContextKind.Marker)
            return;
        EditMarker(new MarkerEdit(IncludeInExport: value));
    }

    partial void OnMarkerOutputNameChanged(string value)
    {
        if (_suppressApply || Kind != PropertiesContextKind.Marker)
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
            param.AddKeyframeCommand.Execute(null);
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
        if (_suppressApply || _markerId is null || Kind != PropertiesContextKind.Marker)
            return;
        _editor.Editor.UpdateMarker(_markerId, name: value);
        }

    [RelayCommand]
    private void SetMarkerColor(string? hex)
    {
        if (_markerId is null || Kind != PropertiesContextKind.Marker || string.IsNullOrEmpty(hex))
            return;
        _editor.Editor.UpdateMarker(_markerId, color: hex);
        _suppressApply = true;
        MarkerColor = hex;
        _suppressApply = false;
    }

    private void ShowTransition(CutTransition transition)
    {
        var displayName = TransitionCatalog.Find(transition.TypeId)?.DisplayName ?? transition.TypeId;

        // Same transition already shown: skip the destructive clear so dragging the duration
        // or a param slider (both raise TimelineChanged per tick) doesn't reset the panel.
        var sameTransition = Kind == PropertiesContextKind.Transition && _transitionKey == transition.Key;
        _suppressApply = true;
        Kind = PropertiesContextKind.Transition;
        Title = "Transition";
        Subtitle = displayName;
        if (!sameTransition)
            ClearSections();
        _clipId = null;
        _transitionKey = transition.Key;
        ShowTransitionSection = true;
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
        _proxyTarget = null;
        ShowProxySection = false;
        CanGenerateProxy = false;
        ResetTabFor(PropertiesContextKind.Transition, transition.Key);
        _suppressApply = false;
    }

    partial void OnTransitionDurationValueChanged(double value)
    {
        if (_suppressApply || _transitionKey is null || Kind != PropertiesContextKind.Transition)
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
        if (_suppressApply || Kind != PropertiesContextKind.Transition)
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
        => Kind == PropertiesContextKind.Transition && _transitionKey is not null
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
        if (_transitionKey is null || Kind != PropertiesContextKind.Transition)
            return;
        var transition = _editor.Editor.GetTransition(_transitionKey);
        if (transition is null)
            return;
        _editor.Editor.RemoveTransition(transition.LeftClipId, transition.RightClipId);
        _editor.Preview.RefreshFrame();
        Refresh();
    }

    // ---- effect stack + schema-driven param editors ----

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

    partial void OnSelectedTabIndexChanged(int value) => Transform?.Refresh();

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
        var clip = _editor.Editor.FindEffectOwner(_clipId);
        if (clip is null)
            return;
        var effect = FindEffect(clip, _selectedEffectId);
        if (effect is null)
            return;
        var entry = EffectCatalog.Find(effect.TypeId);
        if (entry is null)
            return;

        var clipId = _clipId;
        foreach (var def in entry.ParamSchema)
        {
            var key = def.Key;
            var editor = CreateParamEditor(clipId, effect, def,
                write: value => WriteEffectParam(clipId, effect.Id, key, value),
                addKeyframe: _ => AddEffectKeyframe(clipId, effect.Id, key),
                clearKeyframes: _ =>
                {
                    _editor.Editor.ClearKeyframes(clipId, effect.Id, key);
                    RefreshParams();
                },
                stepKeyframe: (_, forward) => StepEffectKeyframe(clipId, effect.Id, key, forward));
            SetEvaluate(editor, clipId, effect.Id, def);
            EffectParams.Add(editor);
        }
        RefreshParams();
    }

    private void SetEvaluate(ParamEditorViewModel editor, string clipId, string effectId, ParamDef def)
    {
        switch (editor)
        {
            case DoubleParamEditorViewModel d:
                d.Evaluate = () => EvalParam(clipId, effectId, def.Key, v => v.AsNumber);
                break;
            case BoolParamEditorViewModel b:
                b.Evaluate = () => EvalParam(clipId, effectId, def.Key, v => v.AsBool);
                break;
            case ColorParamEditorViewModel c:
                c.Evaluate = () => EvalParam(clipId, effectId, def.Key, v => v.AsColor);
                break;
            case ChoiceParamEditorViewModel ch:
                ch.Evaluate = () => EvalParam(clipId, effectId, def.Key, v => v.AsChoice);
                break;
        }
    }

    private static ParamEditorViewModel CreateParamEditor(string clipId, EffectInstance effect, ParamDef def,
        Action<ParamValue> write, Action<string> addKeyframe, Action<string> clearKeyframes, Action<string, bool> stepKeyframe)
    {
        switch (def.Kind)
        {
            case ParamKind.Int:
                return new DoubleParamEditorViewModel(def.Key, def.Label, def.Min, def.Max, true, write, addKeyframe, clearKeyframes, stepKeyframe);
            case ParamKind.Bool:
                return new BoolParamEditorViewModel(def.Key, def.Label, write, addKeyframe, clearKeyframes, stepKeyframe);
            case ParamKind.Color:
                return new ColorParamEditorViewModel(def.Key, def.Label, write, addKeyframe, clearKeyframes, stepKeyframe);
            case ParamKind.List:
                return new ChoiceParamEditorViewModel(def.Key, def.Label, def.Choices, write, addKeyframe, clearKeyframes, stepKeyframe);
            default:
                return new DoubleParamEditorViewModel(def.Key, def.Label, def.Min, def.Max, false, write, addKeyframe, clearKeyframes, stepKeyframe);
        }
    }

    private void RefreshParams()
    {
        foreach (var editor in EffectParams)
            editor.RefreshValue();
    }

    private (T Value, int Count) EvalParam<T>(string clipId, string effectId, string key, Func<ParamValue, T> convert)
    {
        var clip = _editor.Editor.FindEffectOwner(clipId);
        if (clip is null)
            return (default!, 0);
        var effect = FindEffect(clip, effectId);
        if (effect is null)
            return (default!, 0);
        var track = effect.Keyframes.TryGetValue(key, out var t) ? t : null;
        var count = track?.Count ?? 0;
        var localT = Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec);
        var value = count > 0 && track is not null
            ? EffectPipeline.Evaluate(track, localT)
            : (effect.Params.TryGetValue(key, out var constant) ? constant : default);
        return (convert(value), count);
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

    private void AddEffectKeyframe(string clipId, string effectId, string key)
    {
        var clip = _editor.Editor.FindEffectOwner(clipId);
        if (clip is null)
            return;
        var effect = FindEffect(clip, effectId);
        if (effect is null)
            return;
        var localT = Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec);
        var value = effect.Keyframes.TryGetValue(key, out var track) && track.Count > 0
            ? EffectPipeline.Evaluate(track, localT)
            : (effect.Params.TryGetValue(key, out var constant) ? constant : default);
        _editor.Editor.SetKeyframe(clipId, effectId, key, localT, value);
        RefreshParams();
    }

    private void StepEffectKeyframe(string clipId, string effectId, string key, bool forward)
    {
        var clip = _editor.Editor.FindEffectOwner(clipId);
        if (clip is null)
            return;
        var effect = FindEffect(clip, effectId);
        if (effect is null || !effect.Keyframes.TryGetValue(key, out var track) || track.Count == 0)
            return;
        var localT = Math.Max(0, _editor.PlayheadTimeSec - clip.StartSec);
        KeyframePoint? target = null;
        if (forward)
        {
            foreach (var k in track)
                if (k.TimeSec > localT + 1e-6)
                {
                    target = k;
                    break;
                }
        }
        else
        {
            for (var i = track.Count - 1; i >= 0; i--)
                if (track[i].TimeSec < localT - 1e-6)
                {
                    target = track[i];
                    break;
                }
        }
        if (target is { } kf)
            _editor.SeekFromUser(clip.StartSec + kf.TimeSec);
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
            var editor = CreateTransitionParamEditor(def,
                write: value => _editor.Editor.SetTransitionParam(transition.LeftClipId, transition.RightClipId, key, value));

            ParamValue current;
            if (transition.Left.TransitionOut?.Params.TryGetValue(key, out var v1) == true)
                current = v1;
            else if (transition.Right.TransitionIn?.Params.TryGetValue(key, out var v2) == true)
                current = v2;
            else
                current = def.DefaultValue();
            SetInitialValue(editor, current);
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

    private static ParamEditorViewModel CreateTransitionParamEditor(ParamDef def, Action<ParamValue> write)
    {
        Action<string> noop = _ => { };
        Action<string, bool> noop2 = (_, _) => { };
        ParamEditorViewModel editor = def.Kind switch
        {
            ParamKind.Int => new DoubleParamEditorViewModel(def.Key, def.Label, def.Min, def.Max, true, write, noop, noop, noop2),
            ParamKind.Bool => new BoolParamEditorViewModel(def.Key, def.Label, write, noop, noop, noop2),
            ParamKind.Color => new ColorParamEditorViewModel(def.Key, def.Label, write, noop, noop, noop2),
            ParamKind.List => new ChoiceParamEditorViewModel(def.Key, def.Label, def.Choices, write, noop, noop, noop2),
            _ => new DoubleParamEditorViewModel(def.Key, def.Label, def.Min, def.Max, false, write, noop, noop, noop2),
        };
        editor.ShowKeyframes = false;
        return editor;
    }

    private static void SetInitialValue(ParamEditorViewModel editor, ParamValue value)
    {
        switch (editor)
        {
            case DoubleParamEditorViewModel d:
                d.SetSuppress(true);
                d.Value = value.AsNumber;
                d.SetSuppress(false);
                break;
            case BoolParamEditorViewModel b:
                b.SetSuppress(true);
                b.Value = value.AsBool;
                b.SetSuppress(false);
                break;
            case ColorParamEditorViewModel c:
                c.SetSuppress(true);
                c.Hex = value.AsColor.ToString("X8");
                c.SetSuppress(false);
                break;
            case ChoiceParamEditorViewModel ch:
                ch.SetSuppress(true);
                ch.Index = value.AsChoice;
                ch.SetSuppress(false);
                break;
        }
    }

    private static EffectInstance? FindEffect(IEffectOwner clip, string effectId)
    {
        foreach (var effect in clip.Effects)
            if (effect.Id == effectId)
                return effect;
        return null;
    }

    /// <summary>Focuses an effect without selecting its owning clip.</summary>
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
        SelectedTabIndex = 4;   // Adjust tab (effects)
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

    private void ClearSections()
    {
        ShowClipSection = false;
        ShowVideoControls = false;
        ShowAudioControls = false;
        ShowFadeControls = false;
        ShowSpeedControls = false;
        ShowTrackSection = false;
        ShowMarkerSection = false;
        ShowTransitionSection = false;
        ShowEffectsSection = false;
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

    /// <summary>First visible tab in the inspector (Video | Audio | Speed | Animate | Adjust | ...).</summary>
    private int FirstVisibleTab()
        => ShowVideoControls ? 0
         : ShowAudioControls ? 1
         : ShowSpeedControls ? 2
         : ShowFadeControls ? 3
         : ShowEffectsSection ? 4
         : ShowTrackSection ? 5
         : ShowMarkerSection ? 6
         : ShowTransitionSection ? 7
         : 8;

    partial void OnOpacityValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || Kind != PropertiesContextKind.Clip)
            return;
        _editor.Editor.SetOpacity(_clipId, value);
        _editor.Preview.RefreshFrame();
    }

    partial void OnFadeInValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || Kind != PropertiesContextKind.Clip)
            return;
        _editor.Editor.SetFadeIn(_clipId, value);
        // Reflect clamp back into the slider without re-entering Apply
        var clip = FindClip(_clipId);
        if (clip is not null && Math.Abs(clip.FadeInSec - value) > 1e-6)
        {
            _suppressApply = true;
            FadeInValue = clip.FadeInSec;
            _suppressApply = false;
        }
        _editor.Preview.RefreshFrame();
    }

    partial void OnFadeOutValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || Kind != PropertiesContextKind.Clip)
            return;
        _editor.Editor.SetFadeOut(_clipId, value);
        var clip = FindClip(_clipId);
        if (clip is not null && Math.Abs(clip.FadeOutSec - value) > 1e-6)
        {
            _suppressApply = true;
            FadeOutValue = clip.FadeOutSec;
            _suppressApply = false;
        }
        _editor.Preview.RefreshFrame();
    }

    partial void OnVolumeValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || Kind != PropertiesContextKind.Clip)
            return;
        _editor.Editor.SetVolume(_clipId, value);
    }

    partial void OnSpeedValueChanged(double value)
    {
        if (_suppressApply || _clipId is null || Kind != PropertiesContextKind.Clip)
            return;
        _editor.Editor.SetSpeed(_clipId, value);
        _editor.Preview.RefreshFrame();
    }

    partial void OnCropLeftChanged(double value) => ApplyCrop();
    partial void OnCropTopChanged(double value) => ApplyCrop();
    partial void OnCropRightChanged(double value) => ApplyCrop();
    partial void OnCropBottomChanged(double value) => ApplyCrop();

    private void ApplyCrop()
    {
        if (_suppressApply || _clipId is null || !ShowVideoControls)
            return;
        _editor.Editor.SetCrop(_clipId, CropLeft, CropTop, CropRight, CropBottom);
        _editor.Preview.RefreshFrame();
    }

    [RelayCommand]
    private void ResetCrop()
    {
        if (_clipId is null || !ShowVideoControls)
            return;
        _suppressApply = true;
        CropLeft = CropTop = CropRight = CropBottom = 0;
        _suppressApply = false;
        _editor.Editor.SetCrop(_clipId, 0, 0, 0, 0);
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
