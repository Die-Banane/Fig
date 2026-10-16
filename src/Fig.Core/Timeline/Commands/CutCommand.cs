using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    public class CutCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly double _atSec;

        /// <summary>
        /// When set, the produced second half gets this link group id (distinct from the
        /// original) so a split breaks the link between the two halves while still pairing
        /// the right video half with its right audio half.
        /// </summary>
        private readonly string? _secondGroupId;

        private Clip? _first;
        private Track? _track;
        private Clip? _second;
        private int _secondIndex;

        private double _snapStart, _snapDur, _snapSrcIn, _snapSrcOut;
        private double _snapFadeIn, _snapFadeOut;
        private double _sourceAdvance;
        private TransitionRef? _snapTransitionIn, _snapTransitionOut;
        private List<Marker> _snapMarkers = new();

        public string Description => $"Cut clip {_clipId} at {_atSec}s";

        public IReadOnlyList<Clip> ProducedClips { get; private set; } = Array.Empty<Clip>();

        public CutCommand(TimelineEditor editor, string clipId, double atSec, string? secondGroupId = null)
        {
            _editor = editor;
            _clipId = clipId;
            _atSec = atSec;
            _secondGroupId = secondGroupId;
        }

        public void Execute()
        {
            _first = _editor.FindClip(_clipId)
                     ?? throw new InvalidOperationException($"Clip '{_clipId}' not found");
            _track = _editor.FindClipTrack(_clipId)!;

            var offset = _atSec - _first.StartSec;
            if (offset <= 0 || offset >= _first.DurSec)
                throw new ArgumentOutOfRangeException(nameof(_atSec), "Cut point must be inside the clip");

            _snapStart = _first.StartSec;
            _snapDur = _first.DurSec;
            _snapSrcIn = _first.SourceIn;
            _snapSrcOut = _first.SourceOut;
            _snapFadeIn = _first.FadeInSec;
            _snapFadeOut = _first.FadeOutSec;
            _snapTransitionIn = _first.TransitionIn?.Clone();
            _snapTransitionOut = _first.TransitionOut?.Clone();
            _snapMarkers = _first.Markers.Select(CopyMarker).ToList();
            _sourceAdvance = (_first.Speed <= 0 ? 1 : _first.Speed)
                * (_first.SourceRate is { } rate ? rate.Fps / _editor.Document.Rate.Fps : 1);

            _second = Split(_first, offset);

            _editor.InsertClip(_track, _second);
            _secondIndex = _track.Clips.IndexOf(_second);

            ProducedClips = new[] { _first, _second };
        }

        public void Undo()
        {
            if (_first is null || _second is null || _track is null)
                return;

            _track.Clips.Remove(_second);

            _first.StartSec = _snapStart;
            _first.DurSec = _snapDur;
            SetSourceRange(_first, _snapSrcIn, _snapSrcOut);
            _first.FadeInSec = _snapFadeIn;
            _first.FadeOutSec = _snapFadeOut;
            _first.TransitionIn = _snapTransitionIn?.Clone();
            _first.TransitionOut = _snapTransitionOut?.Clone();
            _first.Markers = _snapMarkers.Select(CopyMarker).ToList();
        }

        public void Redo()
        {
            if (_first is null || _second is null || _track is null)
                return;

            var offset = _atSec - _snapStart;
            ApplyFirstHalf(_first, offset);

            if (!_track.Clips.Contains(_second))
                _editor.InsertClip(_track, _second);
        }

        private Clip Split(Clip clip, double offset)
        {
            ApplyFirstHalf(clip, offset);
            return CloneSecondHalf(clip, offset);
        }

        private void ApplyFirstHalf(Clip clip, double offset)
        {
            clip.DurSec = offset;
            SetSourceRange(clip, _snapSrcIn, _snapSrcIn + offset * _sourceAdvance);
            clip.FadeInSec = _snapFadeIn;
            ClipFade.ApplySplitLeft(clip);
            // left keeps transition-in; clears transition-out (cut breaks the out edge)
            clip.TransitionIn = _snapTransitionIn?.Clone();
            clip.TransitionOut = null;
            // markers before the cut stay with the left half, at their original offsets
            clip.Markers = _snapMarkers.Where(m => m.StartSec < offset).Select(CopyMarker).ToList();
        }

        private Clip CloneSecondHalf(Clip first, double offset)
        {
            // Prefer ClipFactory so effects / crops stay in lockstep, then fix range + fades.
            var result = ClipFactory.Clone(first);
            result.StartSec = first.StartSec + offset;
            result.DurSec = _snapDur - offset;
            result.FadeInSec = 0;
            result.FadeOutSec = _snapFadeOut;
            ClipFactory.SetSourceRange(result, _snapSrcIn + offset * _sourceAdvance, _snapSrcOut);
            ClipFade.ApplySplitRight(result);
            // right keeps transition-out; clears transition-in
            result.TransitionIn = null;
            result.TransitionOut = _snapTransitionOut?.Clone();
            // break the link: the right half gets a fresh group id (or none if unlinked)
            result.LinkGroupId = _secondGroupId;
            // markers at/after the cut move to the right half, re-anchored to its start
            result.Markers = _snapMarkers.Where(m => m.StartSec >= offset).Select(m =>
            {
                var marker = CopyMarker(m);
                marker.StartSec -= offset;
                return marker;
            }).ToList();
            return result;
        }

        private static void SetSourceRange(Clip clip, double inSec, double outSec) => clip.SetSourceRange(inSec, outSec);

        // Splitting redistributes markers; it doesn't duplicate them. Keep their identities
        // so multi-selection and subsequent marker edits survive execute/undo/redo.
        private static Marker CopyMarker(Marker original)
        {
            var copy = original.Clone();
            copy.Id = original.Id;
            return copy;
        }
    }
}
