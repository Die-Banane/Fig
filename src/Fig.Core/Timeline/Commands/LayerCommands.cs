using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>Puts a new clip on a track (and takes it off again on undo).</summary>
    public sealed class AddClipCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _trackId;
        private readonly Clip _clip;

        public AddClipCommand(TimelineEditor editor, string trackId, Clip clip)
        {
            _editor = editor;
            _trackId = trackId;
            _clip = clip;
        }

        public string Description => $"Add {_clip.DisplayName.ToLowerInvariant()}";

        public void Execute()
        {
            var track = _editor.FindTrack(_trackId) ?? throw new InvalidOperationException($"Track '{_trackId}' not found");
            _editor.InsertClip(track, _clip);
        }

        public void Undo()
        {
            if (_editor.FindTrack(_trackId) is { } track)
                _editor.RemoveClip(track, _clip);
            _editor.Selection.Deselect(_clip.Id);
        }

        public void Redo() => Execute();
    }

    /// <summary>
    /// Sets a value on several clips at once (the same one, or one each), as one undo step; dragging a slider or
    /// a gizmo coalesces into the same step. Used when an inspector edit applies to everything selected.
    /// </summary>
    public sealed class SetClipValueCommand<T> : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _property;
        private readonly Func<Clip, T> _get;
        private readonly Action<Clip, T> _set;
        private readonly Dictionary<string, T> _old = new();
        private Dictionary<string, T> _values;

        public SetClipValueCommand(TimelineEditor editor, IEnumerable<string> clipIds, string property,
            Func<Clip, T> get, Action<Clip, T> set, T value)
            : this(editor, clipIds.Distinct().ToDictionary(id => id, _ => value), property, get, set)
        {
        }

        public SetClipValueCommand(TimelineEditor editor, IReadOnlyDictionary<string, T> values, string property,
            Func<Clip, T> get, Action<Clip, T> set)
        {
            _editor = editor;
            _values = new Dictionary<string, T>(values);
            _property = property;
            _get = get;
            _set = set;
        }

        public string Description => $"Set {_property}";

        public void Execute()
        {
            _old.Clear();
            foreach (var (id, value) in _values)
                if (_editor.FindClip(id) is { } clip)
                {
                    _old[id] = _get(clip);
                    _set(clip, value);
                }
        }

        public void Undo()
        {
            foreach (var (id, value) in _old)
                if (_editor.FindClip(id) is { } clip)
                    _set(clip, value);
        }

        public void Redo() => Apply();

        public bool CanCoalesceWith(IEditCommand other)
            => other is SetClipValueCommand<T> o && o._property == _property
               && o._values.Count == _values.Count && o._values.Keys.All(_values.ContainsKey);

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is not SetClipValueCommand<T> o)
                return;
            _values = new Dictionary<string, T>(o._values);
            Apply();
        }

        private void Apply()
        {
            foreach (var id in _old.Keys)
                if (_editor.FindClip(id) is { } clip && _values.TryGetValue(id, out var value))
                    _set(clip, value);
        }
    }
}
