using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Markers on clips, tracks and the timeline: adding, picking, editing, moving and ranges.
    /// </summary>
    public partial class TimelineEditor
    {
        // ---- markers ----

        public IEnumerable<MarkerLocation> AllMarkers()
        {
            foreach (var marker in Document.Markers)
                yield return new MarkerLocation(marker, null, null, Document);
            foreach (var track in Document.Tracks)
            {
                foreach (var marker in track.Markers)
                    yield return new MarkerLocation(marker, null, track, Document);
                foreach (var clip in track.Clips)
                    foreach (var marker in clip.Markers)
                        yield return new MarkerLocation(marker, clip, track, Document);
            }
        }

        public void SelectMarker(string id, bool toggle = false, bool extend = false, IReadOnlyCollection<string>? visibleIds = null)
        {
            if (FindMarker(id) is null)
                return;
            if (extend)
            {
                var ordered = AllMarkers().Where(m => visibleIds is null || visibleIds.Contains(m.Marker.Id))
                    .OrderBy(m => (m.Clip?.StartSec ?? 0) + m.Marker.StartSec)
                    .ThenBy(m => m.Marker.Id).Select(m => m.Marker.Id).ToArray();
                Selection.SelectMarkerRange(id, ordered, toggle);
            }
            else if (toggle)
                Selection.ToggleMarker(id);
            else
                Selection.SelectMarker(id);
        }

        /// <summary>Resolves where a marker lives. Returns null when it no longer exists.</summary>
        public MarkerLocation? FindMarker(string markerId)
        {
            foreach (var m in Document.Markers)
                if (m.Id == markerId)
                    return new MarkerLocation(m, null, null, Document);

            foreach (var track in Document.Tracks)
            {
                foreach (var m in track.Markers)
                    if (m.Id == markerId)
                        return new MarkerLocation(m, null, track, Document);
                foreach (var clip in track.Clips)
                    foreach (var m in clip.Markers)
                        if (m.Id == markerId)
                            return new MarkerLocation(m, clip, track, Document);
            }
            return null;
        }

        /// <summary>Adds a marker on a clip at a local offset, clamped into the clip's range.</summary>
        public Marker AddMarker(Clip clip, double localSec, string name = "", string color = "#ffd60a")
        {
            localSec = Math.Clamp(localSec, 0, clip.DurSec);
            var cmd = new AddMarkerCommand(clip, null, Document, localSec, name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>Adds clip-local beat times as one undoable operation.</summary>
        public IReadOnlyList<Marker> AddBeatMarkers(Clip clip, IReadOnlyList<double> times)
        {
            var command = new GenerateMarkersFromBeats(this, clip, times);
            if (!command.HasChanges)
                return Array.Empty<Marker>();
            History.Execute(command);
            RaiseChanged();
            return command.Markers;
        }

        /// <summary>Adds a marker on a track at an absolute timeline time.</summary>
        public Marker AddMarker(Track track, double sec, string name = "", string color = "#ffd60a")
        {
            var cmd = new AddMarkerCommand(null, track, Document, Math.Max(0, sec), name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>Adds a marker on the timeline at an absolute time.</summary>
        public Marker AddMarker(Timeline timeline, double sec, string name = "", string color = "#ffd60a")
        {
            var cmd = new AddMarkerCommand(null, null, timeline, Math.Max(0, sec), name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        public void DeleteMarker(string markerId)
        {
            DeleteMarkers(new[] { markerId });
        }

        public int DeleteMarkers(IEnumerable<string> markerIds)
        {
            var ids = markerIds.Distinct().Where(id => FindMarker(id) is not null).ToArray();
            if (ids.Length == 0)
                return 0;
            History.Execute(new CompositeCommand(ids.Select(id => (IEditCommand)new DeleteMarkerCommand(this, id)).ToArray()));
            if (Selection.SelectedMarkerIds.Any(ids.Contains))
                Selection.SelectMarkers(Selection.SelectedMarkerIds.Except(ids).ToArray());
            RaiseChanged();
            return ids.Length;
        }

        public int ClearClipMarkers(Clip clip, bool generatedOnly = false)
        {
            return DeleteMarkers(clip.Markers.Where(marker => !generatedOnly
                || marker.IsGeneratedBeat).Select(marker => marker.Id).ToArray());
        }

        /// <summary>Adds a range marker to the timeline (one undo step).</summary>
        public Marker AddRangeMarker(double startSec, double durSec, string name = "", string color = "#5f9cf8")
        {
            var cmd = new AddRangeMarkerCommand(Document, startSec, durSec, name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>
        /// Creates a range from a timeline bookmark: with <paramref name="asStart"/> it runs from the
        /// bookmark to the next bookmark (or the end of the sequence); otherwise from the previous
        /// bookmark (or the start) to this one. Returns null when the bookmark is not a timeline
        /// point marker or there is no room for a range.
        /// </summary>
        public Marker? AddRangeFromBookmark(string markerId, bool asStart)
        {
            var bookmark = Document.Markers.FirstOrDefault(m => m.Id == markerId && !m.IsRange);
            if (bookmark is null)
                return null;
            var others = Document.Markers.Where(m => !m.IsRange && m.Id != bookmark.Id).ToList();
            string fromName, toName;
            double from, to;
            if (asStart)
            {
                var next = others.Where(m => m.StartSec > bookmark.StartSec + 1e-3).OrderBy(m => m.StartSec).FirstOrDefault();
                from = bookmark.StartSec;
                to = next?.StartSec ?? BookmarkRanges.SequenceEndSec(Document);
                fromName = bookmark.Name;
                toName = next?.Name ?? "Sequence end";
            }
            else
            {
                var previous = others.Where(m => m.StartSec < bookmark.StartSec - 1e-3).OrderByDescending(m => m.StartSec).FirstOrDefault();
                from = previous?.StartSec ?? 0;
                to = bookmark.StartSec;
                fromName = previous?.Name ?? "Sequence start";
                toName = bookmark.Name;
            }
            if (to - from < 1e-3)
                return null;
            var name = $"{(string.IsNullOrWhiteSpace(fromName) ? "Bookmark" : fromName)} \u2192 {(string.IsNullOrWhiteSpace(toName) ? "Bookmark" : toName)}";
            return AddRangeMarker(from, to - from, name, bookmark.Color);
        }

        /// <summary>Changes several marker fields at once (drags coalesce into one undo step).</summary>
        public void EditMarker(string markerId, MarkerEdit edit)
        {
            if (FindMarker(markerId) is null)
                return;
            History.ExecuteCoalescing(new EditMarkerCommand(this, markerId, edit));
            RaiseChanged();
        }

        /// <summary>
        /// Creates one range marker per section between bookmarks. Existing markers are kept.
        /// All new ranges undo together. Returns the markers created.
        /// </summary>
        public IReadOnlyList<Marker> CreateRangesFromBookmarks(bool includeSequenceBounds)
        {
            var plan = BookmarkRanges.Plan(Document, includeSequenceBounds);
            if (plan.Count == 0)
                return Array.Empty<Marker>();
            var commands = plan
                .Select(r => new AddRangeMarkerCommand(Document, r.StartSec, r.EndSec - r.StartSec, r.Name, r.Color))
                .ToList();
            History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands.ToArray<IEditCommand>()));
            RaiseChanged();
            return commands.Select(c => c.Marker).ToList();
        }

        public void MoveMarker(string markerId, double newSec)
        {
            var loc = FindMarker(markerId);
            if (loc is null)
                return;
            var clamped = loc.Clip is not null ? Math.Clamp(newSec, 0, loc.Clip.DurSec) : Math.Max(0, newSec);
            History.ExecuteCoalescing(new MoveMarkerCommand(this, markerId, clamped));
            RaiseChanged();
        }

        public void UpdateMarker(string markerId, string? name = null, string? color = null)
        {
            if (FindMarker(markerId) is null || (name is null && color is null))
                return;
            History.Execute(new UpdateMarkerCommand(this, markerId, name, color));
            RaiseChanged();
        }
    }
}
