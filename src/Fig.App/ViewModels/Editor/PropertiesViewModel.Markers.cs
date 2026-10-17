using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>
/// The Marker tab: a marker's name, colour, times, export settings and actions.
/// </summary>
public partial class PropertiesViewModel
{
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

    /// <summary>Color swatches offered in the marker inspector (blue, teal, green, amber, red).</summary>
    public static readonly string[] MarkerColors = ["#5f9cf8", "#5ba3b0", "#7fc8a0", "#eab308", "#ff6b6b"];

    public ObservableCollection<MarkerSwatchViewModel> MarkerSwatches { get; } = new();

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
}
