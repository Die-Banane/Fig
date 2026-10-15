using System;
using System.Collections.Generic;
using Fig.Core.Media;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Where a marker lives: on a clip (offset from clip start), a track (absolute),
    /// or the timeline (absolute). <see cref="TimelineEditor.FindMarker"/> resolves this.
    /// </summary>
    public sealed record MarkerLocation(Marker Marker, Clip? Clip, Track? Track, Timeline Timeline)
    {
        public void Add() => OwnerList().Add(Marker);

        public void Remove() => OwnerList().Remove(Marker);

        private List<Marker> OwnerList()
        {
            if (Clip is not null)
                return Clip.Markers;
            if (Track is not null)
                return Track.Markers;
            return Timeline.Markers;
        }
    }

    /// <summary>Creates a marker on a clip (local offset), track, or timeline.</summary>
    public sealed class AddMarkerCommand : IEditCommand
    {
        private readonly Clip? _clip;
        private readonly Track? _track;
        private readonly Timeline _timeline;
        private readonly double _sec;
        private readonly string _name;
        private readonly string _color;
        private Marker _marker = null!;

        public string Description => "Add marker";
        public Marker Marker => _marker;

        public AddMarkerCommand(Clip? clip, Track? track, Timeline timeline, double sec, string name, string color)
        {
            _clip = clip;
            _track = track;
            _timeline = timeline;
            _sec = sec;
            _name = name;
            _color = color;
        }

        public void Execute()
        {
            _marker = new Marker { Name = _name, StartSec = _sec, Color = _color };
            Owner().Add(_marker);
        }

        public void Undo() => Owner().Remove(_marker);

        public void Redo() => Owner().Add(_marker);

        private List<Marker> Owner()
        {
            if (_clip is not null)
                return _clip.Markers;
            if (_track is not null)
                return _track.Markers;
            return _timeline.Markers;
        }
    }

    /// <summary>Removes a marker by id, remembering where it lived for undo.</summary>
    public sealed class DeleteMarkerCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _markerId;
        private MarkerLocation? _location;
        private Marker? _marker;
        private int _index;

        private List<Marker> Owner => _location!.Clip?.Markers ?? _location.Track?.Markers ?? _location.Timeline.Markers;

        public string Description => "Delete marker";

        public DeleteMarkerCommand(TimelineEditor editor, string markerId)
        {
            _editor = editor;
            _markerId = markerId;
        }

        public void Execute()
        {
            _location = _editor.FindMarker(_markerId);
            if (_location is null)
                return;
            _marker = _location.Marker;
            _index = Owner.IndexOf(_marker);
            _location.Remove();
        }

        public void Undo()
        {
            if (_location is not null && _marker is not null)
                Owner.Insert(Math.Min(_index, Owner.Count), _marker);
        }

        public void Redo()
        {
            _location?.Remove();
        }
    }

    /// <summary>
    /// Moves a marker in time. Drag updates coalesce into a single undo step.
    /// </summary>
    public sealed class MoveMarkerCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _markerId;
        private double _newSec;
        private MarkerLocation? _location;
        private double _oldSec;

        public string Description => "Move marker";
        public string MarkerId => _markerId;

        public MoveMarkerCommand(TimelineEditor editor, string markerId, double newSec)
        {
            _editor = editor;
            _markerId = markerId;
            _newSec = newSec;
        }

        public void Execute()
        {
            _location = _editor.FindMarker(_markerId);
            if (_location is null)
                return;
            _oldSec = _location.Marker.StartSec;
            _location.Marker.StartSec = Clamp(_newSec);
        }

        public void Undo()
        {
            if (_location is not null)
                _location.Marker.StartSec = _oldSec;
        }

        public void Redo()
        {
            if (_location is not null)
                _location.Marker.StartSec = Clamp(_newSec);
        }

        public bool CanCoalesceWith(IEditCommand other)
            => other is MoveMarkerCommand m && m._markerId == _markerId;

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is MoveMarkerCommand m)
            {
                _newSec = m._newSec;
                if (_location is not null)
                    _location.Marker.StartSec = Clamp(_newSec);
            }
        }

        private double Clamp(double sec)
            => _location?.Clip is not null ? Math.Clamp(sec, 0, _location.Clip.DurSec) : Math.Max(0, sec);
    }

    /// <summary>Renames and/or recolors an existing marker.</summary>
    public sealed class UpdateMarkerCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _markerId;
        private readonly string? _name;
        private readonly string? _color;
        private MarkerLocation? _location;
        private string? _oldName;
        private string? _oldColor;

        public string Description => "Edit marker";

        public UpdateMarkerCommand(TimelineEditor editor, string markerId, string? name, string? color)
        {
            _editor = editor;
            _markerId = markerId;
            _name = name;
            _color = color;
        }

        public void Execute()
        {
            _location = _editor.FindMarker(_markerId);
            if (_location is null)
                return;
            _oldName = _location.Marker.Name;
            _oldColor = _location.Marker.Color;
            Apply(_name, _color);
        }

        public void Undo()
        {
            if (_location is not null)
                Apply(_oldName, _oldColor);
        }

        public void Redo()
        {
            if (_location is not null)
                Apply(_name, _color);
        }

        private void Apply(string? name, string? color)
        {
            if (name is not null)
                _location!.Marker.Name = name;
            if (color is not null)
                _location!.Marker.Color = color;
        }
    }

    /// <summary>Adds a batch of clip-local beat markers as one undoable edit.</summary>
    public sealed class GenerateMarkersFromBeats : IEditCommand
    {
        private readonly AudioClip _clip;
        private readonly List<Marker> _markers;
        private readonly List<(Marker Marker, int Index)> _replaced;

        public GenerateMarkersFromBeats(TimelineEditor editor, AudioClip clip, IReadOnlyList<double> times)
        {
            _clip = clip;
            _replaced = clip.Markers.Select((marker, index) => (Marker: marker, Index: index))
                .Where(item => item.Marker.IsGeneratedBeat).ToList();
            _markers = CleanTimes(times, clip.DurSec)
                .Select((t, index) => new Marker
                {
                    StartSec = t,
                    Name = $"Beat {index + 1}",
                    Color = "#ffd60a",
                    Metadata = new() { [Marker.BeatAnalysisKey] = System.Text.Json.JsonSerializer.SerializeToElement(true) }
                }).ToList();
        }

        /// <summary>The clip-local times that become markers: finite, inside the clip, distinct and ordered.</summary>
        public static List<double> CleanTimes(IEnumerable<double> times, double clipDurSec)
            => times.Where(t => double.IsFinite(t) && t >= 0 && t < clipDurSec).Distinct().OrderBy(t => t).ToList();

        public string Description => "Generate markers from beats";
        public IReadOnlyList<Marker> Markers => _markers;
        public bool HasChanges => _markers.Count > 0 || _replaced.Count > 0;
        public void Execute()
        {
            foreach (var item in _replaced)
                _clip.Markers.Remove(item.Marker);
            _clip.Markers.AddRange(_markers);
        }
        public void Undo()
        {
            foreach (var marker in _markers)
                _clip.Markers.Remove(marker);
            foreach (var item in _replaced)
                _clip.Markers.Insert(Math.Min(item.Index, _clip.Markers.Count), item.Marker);
        }
        public void Redo() => Execute();
    }

    /// <summary>
    /// Flips the Enabled flag on every clip in the given link groups (undo/redo safe).
    /// </summary>
    public sealed class ToggleEnabledCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly List<string> _seeds;
        private readonly List<(Clip Clip, bool Old)> _toggled = new();

        public string Description => "Toggle clip enabled";

        public ToggleEnabledCommand(TimelineEditor editor, IReadOnlyCollection<string> groupSeeds)
        {
            _editor = editor;
            _seeds = new List<string>(groupSeeds);
        }

        public void Execute()
        {
            _toggled.Clear();
            foreach (var seed in _seeds)
            {
                foreach (var clip in _editor.LinkGroup(seed))
                {
                    if (_toggled.Exists(t => ReferenceEquals(t.Clip, clip)))
                        continue;
                    _toggled.Add((clip, clip.Enabled));
                    clip.Enabled = !clip.Enabled;
                }
            }
        }

        public void Undo()
        {
            foreach (var (clip, old) in _toggled)
                clip.Enabled = old;
        }

        public void Redo()
        {
            foreach (var (clip, old) in _toggled)
                clip.Enabled = !old;
        }
    }
}
