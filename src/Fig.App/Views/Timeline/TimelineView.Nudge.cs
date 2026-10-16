using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>
    /// Trimming into a neighbour pushes it instead of overlapping it. Lengthening a clip's end pushes the clips
    /// after it to the right, and pulling its start earlier pushes the clips before it to the left, only as far as
    /// they are touched, and a pushed clip pushes the next. Linked partners move with the clip they belong to.
    /// Every step works from where the clips were when the trim began, so trimming back lets them return.
    /// </summary>
    public partial class TimelineView
    {
        private Dictionary<string, double> _nudgeStarts = new();

        /// <summary>A trim begins: remember where every clip was, and record them all for the undo step.</summary>
        private void BeginNudge()
        {
            if (Editor is null)
                return;
            var all = Editor.Document.Tracks.SelectMany(t => t.Clips).ToList();
            _nudgeStarts = all.ToDictionary(c => c.Id, c => c.StartSec);
            var members = _dragMembers.Select(m => m.Id).ToHashSet();
            // the trimmed clips first, then everything the trim might push
            _dragBefore = _dragMembers.Select(ClipGeometry.Capture)
                .Concat(all.Where(c => !members.Contains(c.Id)).Select(ClipGeometry.Capture)).ToList();
        }

        /// <summary>
        /// Puts every pushed clip back where it was before working out the next step, so the trimmed edge snaps to
        /// where its neighbours really are, not to where the last step pushed them.
        /// </summary>
        private void UndoNudge()
        {
            if (Editor is null)
                return;
            var members = _dragMembers.Select(m => m.Id).ToHashSet();
            foreach (var clip in Editor.Document.Tracks.SelectMany(t => t.Clips))
                if (!members.Contains(clip.Id) && _nudgeStarts.TryGetValue(clip.Id, out var start))
                    clip.StartSec = start;
        }

        private double StartWhenBegun(Clip clip) => _nudgeStarts.TryGetValue(clip.Id, out var start) ? start : clip.StartSec;

        /// <summary>
        /// Pushes the clips the trimmed ones now overlap (right of them when <paramref name="pushRight"/>, else left).
        /// Returns how far before the timeline's start a leftward push would have had to go (0 when it fits).
        /// </summary>
        private double Nudge(bool pushRight)
        {
            if (Editor is null)
                return 0;
            var members = _dragMembers.Select(m => m.Id).ToHashSet();
            var starts = new Dictionary<Clip, double>();
            foreach (var clip in Editor.Document.Tracks.SelectMany(t => t.Clips))
                starts[clip] = members.Contains(clip.Id) ? clip.StartSec : StartWhenBegun(clip);

            void Shift(Clip clip, double by)
            {
                foreach (var partner in Editor.LinkGroup(clip.Id))
                    if (!members.Contains(partner.Id) && starts.ContainsKey(partner))
                        starts[partner] += by;
            }

            // a push can make the pushed clip (or its partner on another track) push the next: repeat until still
            for (var pass = 0; pass < 64; pass++)
            {
                var moved = false;
                foreach (var track in Editor.Document.Tracks)
                {
                    var ordered = track.Clips.OrderBy(StartWhenBegun).ToList();
                    for (var k = 0; k < ordered.Count - 1; k++)
                    {
                        // rightward pushes run left to right, leftward ones right to left, so a chain moves in one pass
                        var i = pushRight ? k : ordered.Count - 2 - k;
                        var (a, b) = (ordered[i], ordered[i + 1]);
                        var overlap = starts[a] + a.DurSec - starts[b];
                        if (overlap <= 1e-9)
                            continue;
                        var pushed = pushRight ? b : a;
                        if (members.Contains(pushed.Id))
                            continue;
                        Shift(pushed, pushRight ? overlap : -overlap);
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }

            var deficit = Math.Max(0, -starts.Values.DefaultIfEmpty(0).Min());
            foreach (var (clip, start) in starts)
                if (!members.Contains(clip.Id))
                    clip.StartSec = Math.Max(0, start);
            return deficit;
        }
    }
}
