using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public class RippleInsertCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _trackId;
        private readonly Clip _clip;
        private readonly double _posSec;

        private Track? _track;
        private double _snappedPos;
        private readonly List<(Clip Clip, double OldStart)> _shifted = new();
        private Clip? _splitLeft;   // clip created when the insert point splits an existing clip
        private Clip? _splitRight;  // right half of that clip (joins the shifted group)
        private double _splitClipOldStart;

        public string Description => $"Ripple-insert clip at {_posSec}s";

        public RippleInsertCommand(TimelineEditor editor, string trackId, Clip clip, double posSec)
        {
            _editor = editor;
            _trackId = trackId;
            _clip = clip;
            _posSec = posSec;
        }

        public void Execute()
        {
            _track = _editor.FindTrack(_trackId)
                     ?? throw new InvalidOperationException($"Track '{_trackId}' not found");

            _snappedPos = FrameMath.SnapToFrame(_posSec, _editor.Document.Rate);
            _clip.StartSec = _snappedPos;

            _shifted.Clear();
            _splitLeft = null;
            _splitRight = null;

            var clips = _track.Clips.OrderBy(c => c.StartSec).ToList();
            foreach (var existing in clips)
            {
                if (_snappedPos > existing.StartSec && _snappedPos < existing.StartSec + existing.DurSec)
                {
                    _splitClipOldStart = existing.StartSec;
                    var offset = _snappedPos - existing.StartSec;
                    var cutSrc = ClipTime.SourceAt(existing, offset);
                    _splitLeft = ClipFactory.CloneWithRange(
                        existing, existing.StartSec, offset, existing.SourceIn, cutSrc);
                    ClipTime.KeepBefore(_splitLeft, offset);
                    ClipFade.ApplySplitLeft(_splitLeft);
                    _splitRight = ClipFactory.CloneWithRange(
                        existing, _snappedPos, existing.DurSec - offset, cutSrc, existing.SourceOut);
                    ClipTime.KeepAfter(_splitRight, offset);
                    ClipFade.ApplySplitRight(_splitRight);
                    _track.Clips.Remove(existing);
                    _editor.InsertClip(_track, _splitLeft);
                    _editor.InsertClip(_track, _splitRight);
                }
            }

            foreach (var existing in _track.Clips.Where(c => c.StartSec >= _snappedPos).ToList())
            {
                _shifted.Add((existing, existing.StartSec));
                existing.StartSec += _clip.DurSec;
            }

            _editor.InsertClip(_track, _clip);
        }

        public void Undo()
        {
            if (_track is null)
                return;

            _track.Clips.Remove(_clip);
            foreach (var (clip, oldStart) in _shifted)
                clip.StartSec = oldStart;

            if (_splitLeft is not null && _splitRight is not null)
            {
                _track.Clips.Remove(_splitLeft);
                _track.Clips.Remove(_splitRight);
                // the clip as it was before the split: the left half, stretched back over the right half's source
                var original = _splitLeft.Clone();
                if (_splitLeft.HasSourceRange)
                    original.SetSourceRange(_splitLeft.SourceIn, _splitRight.SourceOut);
                original.StartSec = _splitClipOldStart;
                original.DurSec = _splitLeft.DurSec + _splitRight.DurSec;
                _editor.InsertClip(_track, original);
            }
        }

        public void Redo()
        {
            if (_track is null)
                return;
            Execute();
        }
    }
}
