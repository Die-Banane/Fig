using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// The one command every keyframe edit goes through, whatever the keyframes belong to. It remembers the host's
    /// keys before and after, so undo and redo put them back exactly. Edits with the same <c>coalesceKey</c> (one
    /// drag) fold into one undo step: each applies to the keys as the last left them.
    /// </summary>
    public sealed class KeyframeEditCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly KeyframeHostRef _host;
        private readonly string? _coalesceKey;
        private readonly Action<KeyframeHost> _mutate;
        private Dictionary<string, List<KeyframePoint>>? _before;
        private Dictionary<string, List<KeyframePoint>>? _after;

        public string Description { get; }

        public KeyframeEditCommand(TimelineEditor editor, KeyframeHostRef host, string description, string? coalesceKey,
            Action<KeyframeHost> mutate)
        {
            _editor = editor;
            _host = host;
            Description = description;
            _coalesceKey = coalesceKey;
            _mutate = mutate;
        }

        public void Execute()
        {
            if (_editor.FindKeyframeHost(_host) is not { } host)
                return;
            _before = Copy(host.Store);
            Apply(host, _mutate);
        }

        public void Undo() => Restore(_before);

        public void Redo() => Restore(_after);

        public bool CanCoalesceWith(IEditCommand other)
            => _coalesceKey is not null && other is KeyframeEditCommand o && o._coalesceKey == _coalesceKey && o._host == _host;

        public void CoalesceFrom(IEditCommand other)
        {
            // the history does not run a coalesced command, so apply its change here
            if (other is KeyframeEditCommand o && _editor.FindKeyframeHost(_host) is { } host)
                Apply(host, o._mutate);
        }

        private void Apply(KeyframeHost host, Action<KeyframeHost> mutate)
        {
            mutate(host);
            foreach (var (_, track) in host.Tracks)
                track.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
            host.Prune();
            host.Changed();
            _after = Copy(host.Store);
        }

        private void Restore(Dictionary<string, List<KeyframePoint>>? state)
        {
            if (state is null || _editor.FindKeyframeHost(_host) is not { } host)
                return;
            host.Store.Clear();
            foreach (var (key, track) in state)
                host.Store[key] = track.ToList();
            host.Changed();
        }

        private static Dictionary<string, List<KeyframePoint>> Copy(Dictionary<string, List<KeyframePoint>> store)
            => store.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
    }
}
