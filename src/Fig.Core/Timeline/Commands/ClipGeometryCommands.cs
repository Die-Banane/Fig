using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>A clip's position and extent on the timeline, and its source range where it has one.</summary>
    public readonly record struct ClipGeometry(string ClipId, double StartSec, double DurSec, double? SrcInSec, double? SrcOutSec)
    {
        public static ClipGeometry Capture(Clip clip) => clip switch
        {
            VideoClip v => new ClipGeometry(clip.Id, clip.StartSec, clip.DurSec, v.SrcInSec, v.SrcOutSec),
            AudioClip a => new ClipGeometry(clip.Id, clip.StartSec, clip.DurSec, a.SrcInSec, a.SrcOutSec),
            _ => new ClipGeometry(clip.Id, clip.StartSec, clip.DurSec, null, null),
        };

        internal void ApplyTo(Clip clip)
        {
            clip.StartSec = StartSec;
            clip.DurSec = DurSec;
            if (SrcInSec is { } i && SrcOutSec is { } o)
                ClipFactory.SetSourceRange(clip, i, o);
        }
    }

    /// <summary>
    /// A drag that moved or trimmed clips live, recorded as one undo step. The clips already show the
    /// "after" state when this is executed.
    /// </summary>
    public sealed class SetClipGeometryCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly IReadOnlyList<ClipGeometry> _before;
        private readonly IReadOnlyList<ClipGeometry> _after;

        public string Description => _before.Count == 1 ? "Move clip" : $"Move {_before.Count} clips";

        public SetClipGeometryCommand(TimelineEditor editor, IReadOnlyList<ClipGeometry> before, IReadOnlyList<ClipGeometry> after)
        {
            _editor = editor;
            _before = before;
            _after = after;
        }

        public void Execute() => Apply(_after);

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(IReadOnlyList<ClipGeometry> state)
        {
            foreach (var g in state)
                if (_editor.FindClip(g.ClipId) is { } clip)
                    g.ApplyTo(clip);
        }
    }

    /// <summary>Changes which clips are linked (move and cut together).</summary>
    public sealed class SetLinkGroupsCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly Dictionary<string, string?> _after;
        private readonly Dictionary<string, string?> _before = new();

        public string Description => "Change linked clips";

        public SetLinkGroupsCommand(TimelineEditor editor, Dictionary<string, string?> after)
        {
            _editor = editor;
            _after = after;
        }

        public void Execute()
        {
            _before.Clear();
            foreach (var (id, group) in _after)
                if (_editor.FindClip(id) is { } clip)
                {
                    _before[id] = clip.LinkGroupId;
                    clip.LinkGroupId = group;
                }
        }

        public void Undo()
        {
            foreach (var (id, group) in _before)
                if (_editor.FindClip(id) is { } clip)
                    clip.LinkGroupId = group;
        }

        public void Redo()
        {
            foreach (var (id, group) in _after)
                if (_editor.FindClip(id) is { } clip)
                    clip.LinkGroupId = group;
        }
    }
}
