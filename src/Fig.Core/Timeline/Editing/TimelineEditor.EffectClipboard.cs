using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Copy and paste effect regions. Effects live on tracks, so this works like the clip clipboard: the selected
    /// bars are copied with their spacing (relative to the earliest), and paste puts them back at the playhead,
    /// each on the track it came from. When they all came from one track, a chosen track of the same kind takes
    /// them instead.
    /// </summary>
    public partial class TimelineEditor
    {
        /// <summary>An effect on the clipboard; its range and keyframes are relative to the earliest copied one.</summary>
        private sealed record CopiedEffect(string TrackId, TrackKind Kind, EffectInstance Effect);

        private List<CopiedEffect> _effectClipboard = new();

        /// <summary>The last copy was of effects, not clips: pasting puts them on tracks at the playhead.</summary>
        public bool ClipboardHoldsEffects { get; private set; }

        public bool HasEffectClipboard => _effectClipboard.Count > 0;

        /// <summary>Copies the selected effect bars. Returns how many.</summary>
        public int CopySelectedEffects()
        {
            var picked = new List<(Track Track, EffectInstance Effect, double Start)>();
            foreach (var (ownerId, effectId) in Selection.SelectedEffects)
            {
                var owner = FindEffectOwner(ownerId);
                if (owner?.Effects.FirstOrDefault(fx => fx.Id == effectId) is not { } fx)
                    continue;
                // an older project's effect on a clip: its track, in timeline seconds
                var track = owner as Track ?? FindClipTrack(ownerId);
                if (track is null)
                    continue;
                var shift = owner is Track ? 0 : owner.StartSec;
                var start = fx.HasRange ? fx.StartSec!.Value + shift : owner.StartSec;
                var copy = fx.Clone();
                if (fx.HasRange)
                    copy.StartSec = start;
                else if (owner is not Track)
                {
                    copy.StartSec = owner.StartSec;   // a whole-clip effect becomes a region over that clip
                    copy.DurSec = owner.DurSec;
                }
                foreach (var key in copy.Keyframes.Keys.ToList())
                    copy.Keyframes[key] = copy.Keyframes[key].Select(k => k with { TimeSec = k.TimeSec + shift }).ToList();
                picked.Add((track, copy, copy.HasRange ? copy.StartSec!.Value : 0));
            }
            if (picked.Count == 0)
                return 0;

            var anchor = picked.Where(p => p.Effect.HasRange).Select(p => p.Start).DefaultIfEmpty(0).Min();
            _effectClipboard = picked.Select(p =>
            {
                var fx = p.Effect;
                if (fx.HasRange)
                    fx.StartSec -= anchor;
                foreach (var key in fx.Keyframes.Keys.ToList())
                    fx.Keyframes[key] = fx.Keyframes[key].Select(k => k with { TimeSec = k.TimeSec - anchor }).ToList();
                return new CopiedEffect(p.Track.Id, p.Track.Kind, fx);
            }).ToList();
            ClipboardHoldsEffects = true;
            return _effectClipboard.Count;
        }

        /// <summary>
        /// Pastes the copied effects so the earliest starts at <paramref name="atSec"/>, keeping their spacing. Each
        /// goes back on the track it came from (or <paramref name="targetTrackId"/>, when they all came from one
        /// track and it is the same kind). One undo step. Returns the pasted effects; they become the selection.
        /// </summary>
        public IReadOnlyList<EffectInstance> PasteEffects(double atSec, string? targetTrackId = null)
        {
            if (_effectClipboard.Count == 0)
                return Array.Empty<EffectInstance>();
            var target = targetTrackId is null ? null : FindTrack(targetTrackId);
            var oneTrack = _effectClipboard.Select(c => c.TrackId).Distinct().Count() == 1;
            atSec = Math.Max(0, atSec);

            var added = new List<(string TrackId, EffectInstance Effect)>();
            using (History.Group())
            {
                foreach (var copied in _effectClipboard)
                {
                    var track = oneTrack && target is not null && target.Kind == copied.Kind
                        ? target
                        : FindTrack(copied.TrackId) ?? Document.Tracks.FirstOrDefault(t => t.Kind == copied.Kind);
                    if (track is null)
                        continue;
                    var fx = copied.Effect.Clone();
                    if (fx.HasRange)
                        fx.StartSec += atSec;
                    foreach (var key in fx.Keyframes.Keys.ToList())
                        fx.Keyframes[key] = fx.Keyframes[key].Select(k => k with { TimeSec = k.TimeSec + atSec }).ToList();
                    History.Execute(new AddEffectCommand(this, track.Id, fx));
                    added.Add((track.Id, fx));
                }
            }
            if (added.Count == 0)
                return Array.Empty<EffectInstance>();
            Selection.SelectEffects(added.Select(a => (a.TrackId, a.Effect.Id)));
            RaiseChanged();
            return added.Select(a => a.Effect).ToList();
        }
    }
}
