using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public class SelectionState
    {
        private readonly HashSet<string> _ids = new();
        private string? _activeTrackId;
        private string? _markerId;
        private readonly HashSet<string> _markerIds = new();
        private string? _markerAnchorId;
        public IReadOnlyList<string> SelectedMarkerIds => _markerIds.ToList();
        public bool IsMarkerSelected(string id) => _markerIds.Contains(id);
        private string? _transitionKey;

        public IReadOnlyList<string> SelectedClipIds => _ids.ToList();

        /// <summary>
        /// Selected marker id. Mutually exclusive with clip/track selection: selecting
        /// a marker clears clip selection and vice versa.
        /// </summary>
        public string? SelectedMarkerId
        {
            get => _markerId;
            set
            {
                if (value is null)
                    SelectMarkers(Array.Empty<string>());
                else
                    SelectMarker(value);
            }
        }

        /// <summary>
        /// Selected cut transition ("{leftClipId}|{rightClipId}"). Mutually exclusive
        /// with clip/track/marker selection.
        /// </summary>
        public string? SelectedTransitionKey
        {
            get => _transitionKey;
            set
            {
                if (_transitionKey == value)
                    return;
                _transitionKey = value;
                Changed?.Invoke();
            }
        }

        public string? ActiveTrackId
        {
            get => _activeTrackId;
            set
            {
                if (_activeTrackId == value)
                    return;
                _activeTrackId = value;
                if (value is not null)
                {
                    _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
                    _transitionKey = null;
                }
                Changed?.Invoke();
            }
        }

        /// <summary>Raised after any selection mutation (clips, markers, transitions, or active track).</summary>
        public event Action? Changed;

        public bool IsSelected(string clipId) => _ids.Contains(clipId);

        public int Count => _ids.Count;

        public void Select(string clipId)
        {
            if (!_ids.Add(clipId))
                return;
            _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
            _transitionKey = null;
            Changed?.Invoke();
        }

        public void Deselect(string clipId)
        {
            if (!_ids.Remove(clipId))
                return;
            Changed?.Invoke();
        }

        public void SelectOnly(string clipId)
        {
            if (_ids.Count == 1 && _ids.Contains(clipId) && _activeTrackId is null && _markerId is null && _transitionKey is null)
                return;
            _ids.Clear();
            _ids.Add(clipId);
            _activeTrackId = null;
            _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
            _transitionKey = null;
            Changed?.Invoke();
        }

        /// <summary>
        /// Replaces the selection with <paramref name="clipIds"/> in one notification
        /// (clears the active track, marker, and transition). Used after split so the
        /// right halves stay selected.
        /// </summary>
        public void SelectClips(IEnumerable<string> clipIds)
        {
            var next = new HashSet<string>(clipIds.Where(id => !string.IsNullOrEmpty(id)));
            if (next.SetEquals(_ids) && _activeTrackId is null && _markerId is null && _transitionKey is null)
                return;
            _ids.Clear();
            foreach (var id in next)
                _ids.Add(id);
            _activeTrackId = null;
            _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
            _transitionKey = null;
            Changed?.Invoke();
        }

        /// <summary>Selects one marker, clearing clip/track/transition selection.</summary>
        public void SelectMarker(string markerId) => SelectMarkers(new[] { markerId }, markerId);

        public void SelectMarkers(IEnumerable<string> markerIds, string? anchorId = null)
        {
            var next = new HashSet<string>(markerIds.Where(id => !string.IsNullOrEmpty(id)));
            _markerAnchorId = anchorId ?? (next.Contains(_markerAnchorId ?? "") ? _markerAnchorId : next.FirstOrDefault());
            if (next.SetEquals(_markerIds) && _ids.Count == 0 && _activeTrackId is null && _transitionKey is null)
                return;
            _markerIds.Clear();
            foreach (var id in next)
                _markerIds.Add(id);
            _markerId = next.LastOrDefault();
            _ids.Clear();
            _activeTrackId = null;
            _transitionKey = null;
            Changed?.Invoke();
        }

        public void ToggleMarker(string markerId)
        {
            var next = new HashSet<string>(_markerIds);
            if (!next.Add(markerId))
                next.Remove(markerId);
            SelectMarkers(next);
        }

        public void SelectMarkerRange(string markerId, IReadOnlyList<string> orderedIds, bool additive = false)
        {
            var ids = orderedIds.ToList();
            int end = ids.IndexOf(markerId);
            int start = ids.IndexOf(_markerAnchorId ?? "");
            if (start < 0 || end < 0)
            {
                SelectMarker(markerId);
                return;
            }
            var range = ids.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1);
            SelectMarkers(additive ? _markerIds.Concat(range).ToArray() : range, _markerAnchorId);
        }

        /// <summary>Selects a cut transition, clearing any clip/track/marker selection.</summary>
        public void SelectTransition(string key)
        {
            if (_transitionKey == key && _ids.Count == 0 && _activeTrackId is null && _markerId is null)
                return;
            _transitionKey = key;
            _ids.Clear();
            _activeTrackId = null;
            _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (_ids.Count == 0 && _activeTrackId is null && _markerId is null && _transitionKey is null)
                return;
            _ids.Clear();
            _activeTrackId = null;
            _markerId = null;
            _markerIds.Clear();
            _markerAnchorId = null;
            _transitionKey = null;
            Changed?.Invoke();
        }
    }
}
