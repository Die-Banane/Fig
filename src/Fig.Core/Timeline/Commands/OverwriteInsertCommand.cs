using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public class OverwriteInsertCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _trackId;
        private readonly Clip _clip;
        private readonly double _posSec;

        private Track? _track;
        private double _snappedPos;

        private sealed class AffectedClip
        {
            public required Clip Original;
            public int Index;
            public required List<Clip> KeptPortions;
        }

        private readonly List<AffectedClip> _affected = new();

        public string Description => $"Overwrite-insert clip at {_posSec}s";

        public OverwriteInsertCommand(TimelineEditor editor, string trackId, Clip clip, double posSec)
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

            _affected.Clear();

            var insertEnd = _snappedPos + _clip.DurSec;

            var overlaps = _track.Clips
                .Where(c => c.StartSec < insertEnd && c.StartSec + c.DurSec > _snappedPos)
                .OrderBy(c => c.StartSec)
                .ToList();

            foreach (var clip in overlaps)
            {
                var affected = new AffectedClip
                {
                    Original = clip,
                    Index = _track.Clips.IndexOf(clip),
                    KeptPortions = new List<Clip>(),
                };

                // the parts outside the inserted span stay, as new clips
                var keepBefore = _snappedPos - clip.StartSec;
                if (keepBefore > 0.0001)
                    affected.KeptPortions.Add(ClipFactory.Before(clip, keepBefore));
                var keepFrom = insertEnd - clip.StartSec;
                if (clip.DurSec - keepFrom > 0.0001)
                    affected.KeptPortions.Add(ClipFactory.After(clip, keepFrom));

                _track.Clips.Remove(clip);
                _affected.Add(affected);
            }

            foreach (var affected in _affected)
                foreach (var portion in affected.KeptPortions)
                    _editor.InsertClip(_track, portion);

            _editor.InsertClip(_track, _clip);
        }

        public void Undo()
        {
            if (_track is null)
                return;

            _track.Clips.Remove(_clip);

            // the overwritten clips were only taken off the track, never changed
            foreach (var portion in _affected.SelectMany(a => a.KeptPortions))
                _track.Clips.Remove(portion);

            foreach (var affected in _affected.OrderBy(a => a.Index))
                _track.Clips.Insert(Math.Min(affected.Index, _track.Clips.Count), affected.Original);
        }

        public void Redo()
        {
            if (_track is null)
                return;

            // the same kept portions as the first time, so a second undo removes them again
            foreach (var affected in _affected)
            {
                _track.Clips.Remove(affected.Original);
                foreach (var portion in affected.KeptPortions)
                    _editor.InsertClip(_track, portion);
            }

            _editor.InsertClip(_track, _clip);
        }
    }
}
