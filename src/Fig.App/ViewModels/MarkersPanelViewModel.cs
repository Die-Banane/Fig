using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>One marker in the Markers library panel.</summary>
public sealed partial class MarkerRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Color { get; }
    public string Subtitle { get; }
    public IRelayCommand SelectCommand { get; }
    public IRelayCommand EditCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    public MarkerRowViewModel(string id, string name, string color, string subtitle, Action<string> select,
        Action<string> edit, Action<string> delete)
    {
        Id = id;
        Name = name;
        Color = color;
        Subtitle = subtitle;
        SelectCommand = new RelayCommand(() => select(id));
        EditCommand = new RelayCommand(() => edit(id));
        DeleteCommand = new RelayCommand(() => delete(id));
    }
}

/// <summary>The Markers panel: every marker on the sequence and on clips, with quick add and export.</summary>
public sealed partial class MarkersPanelViewModel : ViewModelBase
{
    private readonly EditorViewModel _host;

    public ObservableCollection<MarkerRowViewModel> SequenceMarkers { get; } = new();
    public ObservableCollection<MarkerRowViewModel> ClipMarkers { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    public bool HasSequenceMarkers => SequenceMarkers.Count > 0;
    public bool HasClipMarkers => ClipMarkers.Count > 0;
    public bool HasAnyMarkers => HasSequenceMarkers || HasClipMarkers;
    public bool NoMarkers => !HasAnyMarkers;
    public bool HasRanges => _host.Editor.Document.Markers.Any(m => m.IsRange);
    public int RangeCount => _host.Editor.Document.Markers.Count(m => m.IsRange);

    public MarkersPanelViewModel(EditorViewModel host) => _host = host;

    partial void OnSearchTextChanged(string value) => Refresh();

    /// <summary>Rebuilds both lists from the timeline (called whenever it or the selection changes).</summary>
    public void Refresh()
    {
        var editor = _host.Editor;
        var query = SearchText.Trim();
        bool Matches(Marker m, string? extra = null) => query.Length == 0
            || m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (extra?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

        var selected = editor.Selection.SelectedMarkerId;

        SequenceMarkers.Clear();
        foreach (var m in editor.Document.Markers.OrderBy(m => m.StartSec).Where(m => Matches(m)))
            SequenceMarkers.Add(Row(m, DescribeSequence(m), selected));
        foreach (var track in editor.Document.Tracks)
        {
            var label = string.IsNullOrWhiteSpace(track.Name)
                ? (track.Kind == TrackKind.Video ? $"V{track.Index + 1}" : $"A{track.Index + 1}")
                : track.Name!;
            foreach (var m in track.Markers.OrderBy(m => m.StartSec).Where(m => Matches(m, label)))
                SequenceMarkers.Add(Row(m, $"Track {label} · {Clock(m.StartSec)}", selected));
        }

        ClipMarkers.Clear();
        foreach (var track in editor.Document.Tracks)
            foreach (var clip in track.Clips)
            {
                if (clip.Markers.Count == 0)
                    continue;
                var clipName = ClipLabel(clip);
                foreach (var m in clip.Markers.OrderBy(m => m.StartSec).Where(m => Matches(m, clipName)))
                    ClipMarkers.Add(Row(m, $"{clipName} · {Clock(clip.StartSec + m.StartSec)}", selected));
            }

        OnPropertyChanged(nameof(HasSequenceMarkers));
        OnPropertyChanged(nameof(HasClipMarkers));
        OnPropertyChanged(nameof(HasAnyMarkers));
        OnPropertyChanged(nameof(NoMarkers));
        OnPropertyChanged(nameof(HasRanges));
        OnPropertyChanged(nameof(RangeCount));
    }

    private MarkerRowViewModel Row(Marker m, string subtitle, string? selectedId)
        => new(m.Id, string.IsNullOrWhiteSpace(m.Name) ? (m.IsRange ? "Range" : "Marker") : m.Name, m.Color, subtitle, _host.SelectMarker, _host.EditMarker, _host.DeleteMarker)
        {
            IsSelected = m.Id == selectedId,
        };

    private static string DescribeSequence(Marker m)
        => m.IsRange
            ? $"Range · {Clock(m.StartSec)} → {Clock(m.EndSec)}"
            : $"Point · {Clock(m.StartSec)}";

    private string ClipLabel(Clip clip)
        => clip switch
        {
            VideoClip v when _host.MediaById.TryGetValue(v.SourceId, out var a) => a.FileName,
            AudioClip au when _host.MediaById.TryGetValue(au.SourceId, out var a) => a.FileName,
            _ => "Clip",
        };

    private static string Clock(double sec)
    {
        var total = (int)Math.Round(Math.Max(0, sec));
        return $"{total / 60:00}:{total % 60:00}";
    }

    [RelayCommand]
    private void AddPoint() => _host.AddTimelinePointMarker();

    [RelayCommand]
    private void AddRange() => _host.AddTimelineRangeMarker();
}
