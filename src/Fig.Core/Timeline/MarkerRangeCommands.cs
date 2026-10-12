using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>A set of optional changes to a marker; null fields are left alone.</summary>
    public sealed record MarkerEdit(
        string? Name = null,
        string? Color = null,
        double? StartSec = null,
        double? DurSec = null,
        bool? IncludeInExport = null,
        string? OutputName = null);

    /// <summary>Creates a range marker (a marker with a duration) on the timeline.</summary>
    public sealed class AddRangeMarkerCommand : IEditCommand
    {
        private readonly Timeline _timeline;
        private readonly Marker _marker;

        public string Description => "Add range marker";
        public Marker Marker => _marker;

        public AddRangeMarkerCommand(Timeline timeline, double startSec, double durSec, string name, string color)
        {
            _timeline = timeline;
            _marker = new Marker
            {
                Name = name,
                StartSec = Math.Max(0, startSec),
                DurSec = Math.Max(0, durSec),
                Color = color,
            };
        }

        public void Execute() => _timeline.Markers.Add(_marker);

        public void Undo() => _timeline.Markers.Remove(_marker);

        public void Redo() => _timeline.Markers.Add(_marker);
    }

    /// <summary>
    /// Applies a <see cref="MarkerEdit"/>. Dragging a range edge produces many of these in a
    /// row; they coalesce into one undo step.
    /// </summary>
    public sealed class EditMarkerCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _markerId;
        private MarkerEdit _edit;
        private MarkerLocation? _location;
        private MarkerEdit? _before;

        public string Description => "Edit marker";

        public EditMarkerCommand(TimelineEditor editor, string markerId, MarkerEdit edit)
        {
            _editor = editor;
            _markerId = markerId;
            _edit = edit;
        }

        public void Execute()
        {
            _location = _editor.FindMarker(_markerId);
            if (_location is null)
                return;
            var m = _location.Marker;
            _before = new MarkerEdit(m.Name, m.Color, m.StartSec, m.DurSec, m.IncludeInExport, m.OutputName ?? "");
            Apply(_edit);
        }

        public void Undo()
        {
            if (_location is not null && _before is not null)
                Apply(_before);
        }

        public void Redo()
        {
            if (_location is not null)
                Apply(_edit);
        }

        public bool CanCoalesceWith(IEditCommand other)
            => other is EditMarkerCommand e && e._markerId == _markerId
               && e._edit.Name is null && e._edit.Color is null && e._edit.IncludeInExport is null && e._edit.OutputName is null
               && _edit.Name is null && _edit.Color is null && _edit.IncludeInExport is null && _edit.OutputName is null;

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is not EditMarkerCommand e)
                return;
            _edit = _edit with
            {
                StartSec = e._edit.StartSec ?? _edit.StartSec,
                DurSec = e._edit.DurSec ?? _edit.DurSec,
            };
            if (_location is not null)
                Apply(_edit);
        }

        private void Apply(MarkerEdit edit)
        {
            var m = _location!.Marker;
            if (edit.Name is not null) m.Name = edit.Name;
            if (edit.Color is not null) m.Color = edit.Color;
            if (edit.StartSec is { } start) m.StartSec = Math.Max(0, start);
            if (edit.DurSec is { } dur) m.DurSec = Math.Max(0, dur);
            if (edit.IncludeInExport is { } include) m.IncludeInExport = include;
            if (edit.OutputName is not null) m.OutputName = edit.OutputName.Length == 0 ? null : edit.OutputName;
        }
    }

    /// <summary>One range proposed from bookmarks.</summary>
    public sealed record PlannedRange(string Name, double StartSec, double EndSec, string Color);

    /// <summary>Turns timeline bookmarks (point markers) into back-to-back ranges.</summary>
    public static class BookmarkRanges
    {
        public static double SequenceEndSec(Timeline timeline)
            => timeline.Tracks.SelectMany(t => t.Clips).Select(c => c.StartSec + c.DurSec).DefaultIfEmpty(0).Max();

        /// <summary>
        /// Each section between two neighbouring bookmarks becomes a range. With
        /// <paramref name="includeSequenceBounds"/> the start and end of the sequence count as
        /// boundaries too. Only timeline bookmarks are used; clip and track markers are ignored.
        /// </summary>
        public static IReadOnlyList<PlannedRange> Plan(Timeline timeline, bool includeSequenceBounds)
        {
            var end = SequenceEndSec(timeline);
            var points = timeline.Markers
                .Where(m => !m.IsRange && m.StartSec <= end + 1e-6)
                .OrderBy(m => m.StartSec)
                .Select(m => (Name: string.IsNullOrWhiteSpace(m.Name) ? "Bookmark" : m.Name.Trim(), m.StartSec, m.Color))
                .ToList();

            var bounds = new List<(string Name, double Sec, string Color)>();
            if (includeSequenceBounds)
                bounds.Add(("Sequence start", 0, "#ffd60a"));
            bounds.AddRange(points.Select(p => (p.Name, p.StartSec, p.Color)));
            if (includeSequenceBounds)
                bounds.Add(("Sequence end", end, "#ffd60a"));

            var ranges = new List<PlannedRange>();
            for (var i = 0; i + 1 < bounds.Count; i++)
            {
                var from = bounds[i];
                var to = bounds[i + 1];
                if (to.Sec - from.Sec < 1e-3)
                    continue;
                ranges.Add(new PlannedRange($"{from.Name} \u2192 {to.Name}", from.Sec, to.Sec,
                    i == 0 && !includeSequenceBounds ? to.Color : from.Color));
            }
            return ranges;
        }
    }
}
