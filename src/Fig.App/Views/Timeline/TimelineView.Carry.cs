using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>
    /// Moving a mixed selection: dragging a clip carries the selected effect bars along, and dragging a track's
    /// effect bar carries the selected clips. The whole drag is one undo step.
    /// </summary>
    public partial class TimelineView
    {
        private IDisposable? _carryGroup;
        private string _carryGesture = "";
        private List<(string OwnerId, string EffectId)> _carryEffects = new();
        private readonly List<Clip> _carryClips = new();
        private readonly Dictionary<string, double> _carryStarts = new();
        private IReadOnlyList<ClipGeometry> _carryBefore = Array.Empty<ClipGeometry>();

        /// <summary>A clip drag begins: the selected bars not riding on a moving clip go with it.</summary>
        private void BeginCarryForClips(IReadOnlyCollection<Clip> moving)
        {
            EndCarry();
            if (Editor is null)
                return;
            var ids = moving.Select(c => c.Id).ToHashSet();
            // a bar on a clip that is moving already moves with its clip
            _carryEffects = Editor.Selection.SelectedEffects.Where(e => !ids.Contains(e.ClipId)).ToList();
            if (_carryEffects.Count == 0)
                return;
            _carryGesture = Guid.NewGuid().ToString("N");
            _carryGroup = Editor.History.Group();
        }

        /// <summary>A track effect bar drag begins: the selected clips go with it.</summary>
        private void BeginCarryForEffects(string draggedOwnerId)
        {
            EndCarry();
            if (Editor is null || Editor.FindEffectOwner(draggedOwnerId) is not Track)
                return;     // a bar on a clip moves inside its clip; carrying clips would move it twice
            foreach (var id in Editor.Selection.SelectedClipIds)
                if (Editor.FindClipForView(id) is { } clip)
                    _carryClips.Add(clip);
            if (_carryClips.Count == 0)
                return;
            foreach (var clip in _carryClips)
                _carryStarts[clip.Id] = clip.StartSec;
            _carryBefore = _carryClips.Select(ClipGeometry.Capture).ToList();
            _carryGroup = Editor.History.Group();
        }

        private bool CarriesClips => _carryClips.Count > 0;

        /// <summary>The bars to slide directly: those on carried clips already move with their clip.</summary>
        private IReadOnlyList<(string OwnerId, string EffectId)> BarsToSlide(IReadOnlyList<(string ClipId, string EffectId)> bars)
            => _carryClips.Count == 0 ? bars : bars.Where(b => _carryClips.All(c => c.Id != b.ClipId)).ToList();

        /// <summary>Moves what is carried by <paramref name="deltaSec"/> from where the drag began.</summary>
        private void Carry(double deltaSec)
        {
            if (Editor is null)
                return;
            if (_carryEffects.Count > 0)
                Editor.MoveEffects(_carryEffects, deltaSec, _carryGesture);
            if (_carryClips.Count == 0)
                return;
            deltaSec = Math.Max(deltaSec, -_carryStarts.Values.Min());     // nothing before the start
            var targets = _carryClips.ToDictionary(c => c.Id, c => _carryStarts[c.Id] + deltaSec);
            if (!Editor.WouldOverlapMove(targets))
                foreach (var clip in _carryClips)
                    clip.StartSec = targets[clip.Id];
        }

        /// <summary>The drag is over: record the carried clips and close the undo step.</summary>
        private void EndCarry()
        {
            if (_carryClips.Count > 0)
                Editor?.CommitClipGeometry(_carryBefore);
            _carryGroup?.Dispose();
            _carryGroup = null;
            _carryEffects = new();
            _carryClips.Clear();
            _carryStarts.Clear();
            _carryBefore = Array.Empty<ClipGeometry>();
        }
    }
}
