using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// What is selected, as one ordered list of keys. Clips, tracks, markers, transitions and effect bars
    /// are all the same kind of entry; the list only ever holds one <see cref="SelectionGroup"/> (picking
    /// something from another group replaces it). The clip/marker/effect members below are views over that
    /// list for the callers that only care about one group.
    /// </summary>
    public class SelectionState
    {
        private readonly List<SelectionKey> _items = new();
        private string? _markerAnchorId;

        /// <summary>Raised after any selection change.</summary>
        public event Action? Changed;

        // ---- the uniform API ------------------------------------------------------------------

        /// <summary>Everything selected, in the order it was picked.</summary>
        public IReadOnlyList<SelectionKey> Items => _items.ToList();

        /// <summary>The last item picked: what a single-item panel shows.</summary>
        public SelectionKey? Primary => _items.Count == 0 ? null : _items[^1];

        public bool IsEmpty => _items.Count == 0;

        public bool Contains(SelectionKey key) => _items.Contains(key);

        /// <summary>Selects exactly this item.</summary>
        public void Replace(SelectionKey key) => Replace(new[] { key });

        /// <summary>Selects exactly these items (which must be of one group; the first item's group wins).</summary>
        public void Replace(IEnumerable<SelectionKey> keys)
        {
            var next = Distinct(keys);
            if (_items.SequenceEqual(next))
                return;
            _items.Clear();
            _items.AddRange(next);
            Changed?.Invoke();
        }

        /// <summary>Adds an item to the selection (or replaces the selection when it is of another group).</summary>
        public void Add(SelectionKey key)
        {
            if (_items.Contains(key))
                return;
            if (_items.Count > 0 && _items[0].Group != key.Group)
                _items.Clear();
            _items.Add(key);
            Changed?.Invoke();
        }

        /// <summary>Takes an item out of the selection.</summary>
        public void Remove(SelectionKey key)
        {
            if (_items.Remove(key))
                Changed?.Invoke();
        }

        /// <summary>Adds the item, or takes it out when it is already selected.</summary>
        public void Toggle(SelectionKey key)
        {
            if (_items.Contains(key))
                Remove(key);
            else
                Add(key);
        }

        public void Clear()
        {
            if (_items.Count == 0)
                return;
            _items.Clear();
            _markerAnchorId = null;
            Changed?.Invoke();
        }

        private static List<SelectionKey> Distinct(IEnumerable<SelectionKey> keys)
        {
            var result = new List<SelectionKey>();
            foreach (var key in keys)
            {
                if (result.Count > 0 && result[0].Group != key.Group)
                    continue;
                if (!result.Contains(key))
                    result.Add(key);
            }
            return result;
        }

        private IEnumerable<SelectionKey> Of(SelectionGroup group) => _items.Where(k => k.Group == group);

        // ---- clips ----------------------------------------------------------------------------

        public IReadOnlyList<string> SelectedClipIds => Of(SelectionGroup.Clip).Select(k => k.Id).ToList();

        public bool IsSelected(string clipId) => _items.Contains(new SelectionKey(SelectionGroup.Clip, clipId));

        /// <summary>How many clips are selected.</summary>
        public int Count => Of(SelectionGroup.Clip).Count();

        public void Select(string clipId) => Add(new SelectionKey(SelectionGroup.Clip, clipId));

        public void Deselect(string clipId) => Remove(new SelectionKey(SelectionGroup.Clip, clipId));

        public void SelectOnly(string clipId) => Replace(new SelectionKey(SelectionGroup.Clip, clipId));

        /// <summary>Replaces the selection with these clips in one notification (used after split so the right halves stay selected).</summary>
        public void SelectClips(IEnumerable<string> clipIds)
            => Replace(clipIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => new SelectionKey(SelectionGroup.Clip, id)));

        // ---- tracks ---------------------------------------------------------------------------

        /// <summary>The selected track, if a track (rather than something on it) is what is selected.</summary>
        public string? ActiveTrackId
        {
            get => Of(SelectionGroup.Track).Select(k => k.Id).FirstOrDefault();
            set
            {
                if (value is null)
                {
                    if (ActiveTrackId is { } current)
                        Remove(new SelectionKey(SelectionGroup.Track, current));
                    return;
                }
                Replace(new SelectionKey(SelectionGroup.Track, value));
            }
        }

        // ---- transitions ----------------------------------------------------------------------

        /// <summary>The selected cut transition ("{leftClipId}|{rightClipId}").</summary>
        public string? SelectedTransitionKey
        {
            get => Of(SelectionGroup.Transition).Select(k => k.Id).FirstOrDefault();
            set
            {
                if (value is null)
                {
                    if (SelectedTransitionKey is { } current)
                        Remove(new SelectionKey(SelectionGroup.Transition, current));
                    return;
                }
                SelectTransition(value);
            }
        }

        /// <summary>Selects a cut transition.</summary>
        public void SelectTransition(string key) => Replace(new SelectionKey(SelectionGroup.Transition, key));

        // ---- markers --------------------------------------------------------------------------

        public IReadOnlyList<string> SelectedMarkerIds => Of(SelectionGroup.Marker).Select(k => k.Id).ToList();

        public bool IsMarkerSelected(string id) => _items.Contains(new SelectionKey(SelectionGroup.Marker, id));

        /// <summary>The primary marker for the inspector; <see cref="SelectedMarkerIds"/> holds the full selection.</summary>
        public string? SelectedMarkerId
        {
            get => Of(SelectionGroup.Marker).Select(k => k.Id).LastOrDefault();
            set
            {
                if (value is null)
                {
                    if (_items.Count > 0 && _items[0].Group == SelectionGroup.Marker)
                    {
                        _markerAnchorId = null;
                        Clear();
                    }
                }
                else
                    SelectMarker(value);
            }
        }

        /// <summary>Selects one marker.</summary>
        public void SelectMarker(string markerId) => SelectMarkers(new[] { markerId }, markerId);

        public void SelectMarkers(IEnumerable<string> markerIds, string? anchorId = null)
        {
            var ids = markerIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            _markerAnchorId = anchorId ?? (ids.Contains(_markerAnchorId ?? "") ? _markerAnchorId : ids.FirstOrDefault());
            Replace(ids.Select(id => new SelectionKey(SelectionGroup.Marker, id)));
        }

        public void ToggleMarker(string markerId)
        {
            var next = SelectedMarkerIds.ToList();
            if (!next.Remove(markerId))
                next.Add(markerId);
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
            // in timeline order, however the markers were picked
            var chosen = additive ? SelectedMarkerIds.Concat(range).Distinct().OrderBy(id => ids.IndexOf(id)).ToArray() : range;
            SelectMarkers(chosen, _markerAnchorId);
        }

        // ---- effect bars ----------------------------------------------------------------------

        /// <summary>The effect bars that are selected (on a track or a clip), in the order they were picked.</summary>
        public IReadOnlyList<(string ClipId, string EffectId)> SelectedEffects
            => Of(SelectionGroup.Effect).Select(k => (k.OwnerId ?? "", k.Id)).ToList();

        /// <summary>The primary effect (the last one picked): what the inspector shows.</summary>
        public (string ClipId, string EffectId)? SelectedEffect
            => Of(SelectionGroup.Effect).Select(k => ((string ClipId, string EffectId)?)(k.OwnerId ?? "", k.Id)).LastOrDefault();

        public bool IsEffectSelected(string effectId) => Of(SelectionGroup.Effect).Any(k => k.Id == effectId);

        /// <summary>Selects exactly one effect bar.</summary>
        public void SelectEffect(string clipId, string effectId) => SelectEffects(new[] { (clipId, effectId) });

        /// <summary>Selects exactly these effect bars, or adds them to the current effect selection.</summary>
        public void SelectEffects(IEnumerable<(string ClipId, string EffectId)> effects, bool additive = false)
        {
            var keys = effects.Select(e => new SelectionKey(SelectionGroup.Effect, e.EffectId, e.ClipId));
            if (additive && _items.Count > 0 && _items[0].Group == SelectionGroup.Effect)
                keys = _items.Concat(keys);
            Replace(keys);
        }

        /// <summary>Adds the effect bar to the selection, or takes it out when it is already in.</summary>
        public void ToggleEffect(string clipId, string effectId) => Toggle(new SelectionKey(SelectionGroup.Effect, effectId, clipId));
    }
}
