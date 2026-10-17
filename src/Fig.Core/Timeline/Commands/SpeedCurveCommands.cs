using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Replaces a clip's speed curve (null removes it) and moves its out point to where the new curve leaves it.
    /// The clip keeps its place and length. A drag of one point is one undo step: commands with the same gesture
    /// coalesce.
    /// </summary>
    public sealed class SetSpeedCurveCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly string _gesture;
        private List<KeyframePoint>? _keys;
        private List<KeyframePoint>? _oldKeys;
        private double _oldSrcIn, _oldSrcOut;
        private bool _captured;

        public string Description { get; }

        public SetSpeedCurveCommand(TimelineEditor editor, string clipId, IEnumerable<KeyframePoint>? keys,
            string description, string gesture)
        {
            _editor = editor;
            _clipId = clipId;
            _keys = keys?.OrderBy(k => k.TimeSec).ToList();
            Description = description;
            _gesture = gesture;
        }

        public void Execute()
        {
            if (_editor.FindClip(_clipId) is not { } clip)
                return;
            if (!_captured)
            {
                _oldKeys = ClipTime.Curve(clip)?.ToList();
                (_oldSrcIn, _oldSrcOut) = (clip.SourceIn, clip.SourceOut);
                _captured = true;
            }
            Apply(clip, _keys);
            if (_keys is not null)
                ClipTime.SyncSourceOut(clip);
            else if (clip.HasSourceRange)
                // back to one speed: the clip plays its length at Speed again
                clip.SetSourceRange(clip.SourceIn, clip.SourceIn + clip.DurSec * (clip.Speed <= 0 ? 1 : clip.Speed));
        }

        public void Undo()
        {
            if (!_captured || _editor.FindClip(_clipId) is not { } clip)
                return;
            Apply(clip, _oldKeys);
            if (clip.HasSourceRange)
                clip.SetSourceRange(_oldSrcIn, _oldSrcOut);
        }

        public void Redo() => Execute();

        public bool CanCoalesceWith(IEditCommand other)
            => other is SetSpeedCurveCommand o && o._clipId == _clipId && o._gesture == _gesture && _gesture.Length > 0;

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is not SetSpeedCurveCommand o)
                return;
            _keys = o._keys;
            Execute();
        }

        private static void Apply(Clip clip, List<KeyframePoint>? keys)
        {
            if (keys is null || keys.Count == 0)
                clip.Keyframes.Remove(AutomationKeys.Speed);
            else
                clip.Keyframes[AutomationKeys.Speed] = keys.ToList();
        }
    }
}
