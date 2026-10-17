using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Copy and paste effects from clip to clip. A video clip's effects are regions on its track over the clip; an
    /// audio clip's are on the clip. Either way they are copied relative to the clip (or, for selected effect bars,
    /// to the earliest of them) and pasted the same way onto each target clip, cut to fit it.
    /// </summary>
    public partial class TimelineEditor
    {
        /// <summary>An effect on the clipboard: its range and keyframes are relative to where it was copied from;
        /// no range means "the whole clip".</summary>
        private sealed record CopiedEffect(EffectInstance Effect, bool Audio);

        private List<CopiedEffect> _effectClipboard = new();

        /// <summary>The last copy was of effects, not clips: pasting puts them on the selected clips.</summary>
        public bool ClipboardHoldsEffects { get; private set; }

        public bool HasEffectClipboard => _effectClipboard.Count > 0;

        /// <summary>The effects over a clip: those on it, and the regions over it on its track (cut to the clip).</summary>
        public IReadOnlyList<EffectInstance> EffectsOver(string clipId)
        {
            if (FindClip(clipId) is not { } clip || FindClipTrack(clipId) is not { } track)
                return Array.Empty<EffectInstance>();
            var end = clip.StartSec + clip.DurSec;
            return clip.Effects
                .Concat(track.Effects.Where(fx => !fx.HasRange
                    || fx.StartSec < end - 1e-6 && fx.StartSec + fx.DurSec > clip.StartSec + 1e-6))
                .ToList();
        }

        /// <summary>Copies the effects over a clip. Returns how many.</summary>
        public int CopyEffects(string clipId)
        {
            if (FindClip(clipId) is not { } clip || FindClipTrack(clipId) is not { } track)
                return 0;
            var audio = IsAudioOwner(clip) || track.Kind == TrackKind.Audio;
            var copied = new List<CopiedEffect>();
            foreach (var fx in clip.Effects)
                copied.Add(new CopiedEffect(fx.Clone(), audio));   // already relative to the clip
            var end = clip.StartSec + clip.DurSec;
            foreach (var fx in track.Effects)
            {
                if (!fx.HasRange)
                {
                    copied.Add(new CopiedEffect(Relative(fx, clip.StartSec, null, null), audio));
                    continue;
                }
                var start = Math.Max(fx.StartSec!.Value, clip.StartSec);
                var stop = Math.Min(fx.StartSec.Value + fx.DurSec!.Value, end);
                if (stop - start <= 1e-6)
                    continue;
                // one that covers the whole clip pastes as covering the whole of the next one
                var whole = start <= clip.StartSec + 1e-6 && stop >= end - 1e-6;
                copied.Add(new CopiedEffect(Relative(fx, clip.StartSec, whole ? null : start, whole ? null : stop - start), audio));
            }
            if (copied.Count == 0)
                return 0;
            _effectClipboard = copied;
            ClipboardHoldsEffects = true;
            return copied.Count;
        }

        /// <summary>Copies the selected effect bars, relative to the earliest of them. Returns how many.</summary>
        public int CopySelectedEffects()
        {
            var picked = Selection.SelectedEffects
                .Select(e => (Owner: FindEffectOwner(e.ClipId), Id: e.EffectId))
                .Select(e => (e.Owner, Effect: e.Owner?.Effects.FirstOrDefault(fx => fx.Id == e.Id)))
                .Where(e => e.Owner is not null && e.Effect is not null)
                .Select(e => (Owner: e.Owner!, Effect: e.Effect!, Start: e.Owner!.StartSec + (e.Effect!.StartSec ?? 0)))
                .ToList();
            if (picked.Count == 0)
                return 0;
            var anchor = picked.Where(p => p.Effect.HasRange).Select(p => p.Start).DefaultIfEmpty(0).Min();
            _effectClipboard = picked.Select(p =>
            {
                var audio = IsAudioOwner(p.Owner);
                if (!p.Effect.HasRange)
                    return new CopiedEffect(Relative(p.Effect, p.Owner is Track ? anchor : 0, null, null), audio);
                // a clip's own effects are timed from the clip; bring them onto the timeline first
                var shift = p.Owner is Track ? 0 : p.Owner.StartSec;
                return new CopiedEffect(Relative(p.Effect, anchor - shift, p.Effect.StartSec, p.Effect.DurSec), audio);
            }).ToList();
            ClipboardHoldsEffects = true;
            return _effectClipboard.Count;
        }

        /// <summary>
        /// Pastes the copied effects onto each of the clips: a video effect onto a video clip, an audio one onto an
        /// audio clip; the others are skipped. Each lands at its offset from the clip start, cut to the clip.
        /// One undo step. Returns how many effects were added; they become the selection.
        /// </summary>
        public int PasteEffects(IEnumerable<string> clipIds)
        {
            var targets = clipIds.Distinct().Select(FindClip).Where(c => c is not null).Cast<Clip>().ToList();
            if (_effectClipboard.Count == 0 || targets.Count == 0)
                return 0;

            var added = new List<(string OwnerId, string EffectId)>();
            using (History.Group())
            {
                foreach (var clip in targets)
                {
                    if (FindClipTrack(clip.Id) is not { } track)
                        continue;
                    var audio = IsAudioOwner(clip) || track.Kind == TrackKind.Audio;
                    foreach (var copied in _effectClipboard.Where(c => c.Audio == audio))
                    {
                        // video effects are regions on the track (timeline seconds); audio ones live on the clip
                        var onTrack = track.Kind == TrackKind.Video;
                        var origin = onTrack ? clip.StartSec : 0;
                        var fx = copied.Effect.Clone();
                        if (fx.HasRange)
                        {
                            var start = Math.Max(fx.StartSec!.Value, 0);
                            var stop = Math.Min(fx.StartSec.Value + fx.DurSec!.Value, clip.DurSec);
                            if (stop - start < 0.05)
                                continue;   // it falls past this clip's end
                            fx.StartSec = origin + start;
                            fx.DurSec = stop - start;
                        }
                        else if (onTrack)
                        {
                            fx.StartSec = clip.StartSec;
                            fx.DurSec = clip.DurSec;
                        }
                        foreach (var key in fx.Keyframes.Keys.ToList())
                            fx.Keyframes[key] = fx.Keyframes[key].Select(k => k with { TimeSec = k.TimeSec + origin }).ToList();
                        var owner = onTrack ? track.Id : clip.Id;
                        History.Execute(new AddEffectCommand(this, owner, fx));
                        added.Add((owner, fx.Id));
                    }
                }
            }
            if (added.Count == 0)
                return 0;
            Selection.SelectEffects(added);
            RaiseChanged();
            return added.Count;
        }

        /// <summary>A copy of <paramref name="fx"/> with its range and keyframes moved <paramref name="anchor"/> earlier.</summary>
        private static EffectInstance Relative(EffectInstance fx, double anchor, double? start, double? dur)
        {
            var copy = fx.Clone();
            copy.StartSec = start is { } s ? s - anchor : null;
            copy.DurSec = start is null ? null : dur;
            foreach (var key in copy.Keyframes.Keys.ToList())
                copy.Keyframes[key] = copy.Keyframes[key].Select(k => k with { TimeSec = k.TimeSec - anchor }).ToList();
            return copy;
        }
    }
}
