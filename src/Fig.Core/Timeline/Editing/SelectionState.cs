using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// What is selected, as one ordered list of keys. Clips, tracks, markers, transitions and effect bars
    /// are all the same kind of entry, and one selection can hold any mix of them (clips and effect bars and
    /// markers at once). A plain pick replaces everything; Ctrl/Shift picks add to it. The clip/marker/effect
    /// members below are views over that list for the callers that only care about one group.
    /// </summary>
    public class SelectionState
    {
        private readonly List<SelectionKey> _items = new();
        private string? _markerAnchorId;

        /// <summary>Raised after any selection change.</summary>
        public event Action? Changed;

        /// <summary>
        /// The keyframe diamond picked on the timeline, of any kind; its owner (clip or effect bar) is the selection.
        /// Any other change to the selection drops it.
        /// </summary>
        public KeyframeRef? Keyframe { get; private set; }

        /// <summary>Selects a keyframe diamond, and its owner with it.</summary>
        public void SelectKeyframe(SelectionKey owner, KeyframeRef keyframe)
        {
            _items.Clear();
            _items.Add(owner);
            Keyframe = keyframe;
            Changed?.Invoke();
        }

        /// <summary>The selected diamond moved (dragged): keep it selected at its new time.</summary>
        public void MoveKeyframe(double timeSec)
        {
            if (Keyframe is { } k)
                Keyframe = k with { TimeSec = timeSec };
        }

        /// <summary>Drops the selected diamond (it was deleted), keeping its owner selected.</summary>
        public void ClearKeyframe()
        {
            if (Keyframe is null)
                return;
            Keyframe = null;
            Changed?.Invoke();
        }

        // ---- the uniform API ------------------------------------------------------------------

        /// <summary>Everything selected, in the order it was picked.</summary>
        public IReadOnlyList<SelectionKey> Items => _items.ToList();

        /// <summary>The last item picked: what a single-item panel shows.</summary>
        public SelectionKey? Primary => _items.Count == 0 ? null : _items[^1];

        public bool IsEmpty => _items.Count == 0;

        public bool Contains(SelectionKey key) => _items.Contains(key);

        /// <summary>Selects exactly this item.</summary>
        public void Replace(SelectionKey key) => Replace(new[] { key });

        /// <summary>Selects exactly these items (of any groups).</summary>
        public void Replace(IEnumerable<SelectionKey> keys)
        {
            var next = Distinct(keys);
            if (_items.SequenceEqual(next))
                return;
            _items.Clear();
            _items.AddRange(next);
            Keyframe = null;
            Changed?.Invoke();
        }

        /// <summary>Adds an item to the selection, whatever else is selected.</summary>
        public void Add(SelectionKey key)
        {
            if (_items.Contains(key))
                return;
            _items.Add(key);
            Keyframe = null;
            Changed?.Invoke();
        }

        /// <summary>Takes an item out of the selection.</summary>
        public void Remove(SelectionKey key)
        {
            if (_items.Remove(key))
            {
                Keyframe = null;
                Changed?.Invoke();
            }
        }

        /// <summary>Adds the item, or takes it out when it is already selected.</summary>
        public void Toggle(SelectionKey key)
        {
            if (_items.Contains(key))
                Remove(key);
            else
                Add(key);
        }

        /// <summary>
        /// Makes <paramref name="keys"/> the selected items of <paramref name="group"/>, leaving the other groups
        /// selected as they are (Ctrl-clicking a clip while effect bars are selected keeps the bars).
        /// </summary>
        public void SetGroup(SelectionGroup group, IEnumerable<SelectionKey> keys)
            => Replace(_items.Where(k => k.Group != group).Concat(keys.Where(k => k.Group == group)));

        public void Clear()
        {
            if (_items.Count == 0 && Keyframe is null)
                return;
            _items.Clear();
            _markerAnchorId = null;
            Keyframe = null;
            Changed?.Invoke();
        }

        private static List<SelectionKey> Distinct(IEnumerable<SelectionKey> keys)
        {
            var result = new List<SelectionKey>();
            foreach (var key in keys)
            {
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
            => Replace(ClipKeys(clipIds));

        /// <summary>Makes these the selected clips, keeping whatever else is selected (a Ctrl/Shift pick).</summary>
        public void SetClips(IEnumerable<string> clipIds) => SetGroup(SelectionGroup.Clip, ClipKeys(clipIds));

        private static IEnumerable<SelectionKey> ClipKeys(IEnumerable<string> clipIds)
            => clipIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => new SelectionKey(SelectionGroup.Clip, id));

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
                    _markerAnchorId = null;
                    SetGroup(SelectionGroup.Marker, Array.Empty<SelectionKey>());
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

        /// <summary>Adds the marker to the selection, or takes it out (anything else selected stays).</summary>
        public void ToggleMarker(string markerId)
        {
            Toggle(new SelectionKey(SelectionGroup.Marker, markerId));
            // Shift-click ranges keep measuring from the anchor while it is still selected
            if (!SelectedMarkerIds.Contains(_markerAnchorId ?? ""))
                _markerAnchorId = SelectedMarkerIds.FirstOrDefault();
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
            if (!additive)
            {
                SelectMarkers(range, _markerAnchorId);
                return;
            }
            var chosen = SelectedMarkerIds.Concat(range).Distinct().OrderBy(id => ids.IndexOf(id));
            SetGroup(SelectionGroup.Marker, chosen.Select(id => new SelectionKey(SelectionGroup.Marker, id)));
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

        /// <summary>Selects exactly these effect bars, or adds them to whatever is selected.</summary>
        public void SelectEffects(IEnumerable<(string ClipId, string EffectId)> effects, bool additive = false)
        {
            var keys = effects.Select(e => new SelectionKey(SelectionGroup.Effect, e.EffectId, e.ClipId));
            Replace(additive ? _items.Concat(keys) : keys);
        }

        /// <summary>Adds the effect bar to the selection, or takes it out when it is already in.</summary>
        public void ToggleEffect(string clipId, string effectId) => Toggle(new SelectionKey(SelectionGroup.Effect, effectId, clipId));
    }
}
