using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Slides several effect bars together by the same amount, keyframes included, so their spacing
    /// is kept. Every step of one drag (same gesture) merges into a single undo step.
    /// </summary>
    public sealed class MoveEffectsCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly IReadOnlyList<(string OwnerId, string EffectId)> _items;
        private readonly string _gesture;
        private double _delta;
        private readonly Dictionary<string, EffectState> _before = new();
        private readonly Dictionary<string, EffectState> _after = new();

        public string Description => "Move effects";

        public MoveEffectsCommand(TimelineEditor editor, IReadOnlyList<(string OwnerId, string EffectId)> items,
            double deltaSec, string gesture)
        {
            _editor = editor;
            _items = items;
            _delta = deltaSec;
            _gesture = gesture;
        }

        public void Execute()
        {
            _before.Clear();
            foreach (var (ownerId, effectId) in _items)
                if (Find(ownerId, effectId) is { } fx && fx.HasRange)
                    _before[effectId] = EffectState.Capture(fx);
            Apply();
        }

        public void Undo()
        {
            foreach (var (ownerId, effectId) in _items)
                if (_before.TryGetValue(effectId, out var state) && Find(ownerId, effectId) is { } fx)
                    state.RestoreTo(fx);
        }

        public void Redo()
        {
            foreach (var (ownerId, effectId) in _items)
                if (_after.TryGetValue(effectId, out var state) && Find(ownerId, effectId) is { } fx)
                    state.RestoreTo(fx);
        }

        public bool CanCoalesceWith(IEditCommand other)
            => other is MoveEffectsCommand o && o._gesture == _gesture
               && o._items.Select(i => i.EffectId).SequenceEqual(_items.Select(i => i.EffectId));

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is not MoveEffectsCommand o)
                return;
            // a drag step carries the total distance from where the drag began, so redo it from the start
            foreach (var (ownerId, effectId) in _items)
                if (_before.TryGetValue(effectId, out var state) && Find(ownerId, effectId) is { } fx)
                    state.RestoreTo(fx);
            _delta = o._delta;
            Apply();
        }

        private void Apply()
        {
            // one shared distance, limited so that no bar leaves the start of time or the end of its clip
            var delta = _delta;
            foreach (var (ownerId, effectId) in _items)
            {
                if (!_before.TryGetValue(effectId, out var state) || _editor.FindEffectOwner(ownerId) is not { } owner)
                    continue;
                var start = state.StartSec ?? 0;
                var dur = state.DurSec ?? 0;
                delta = Math.Max(delta, -start);
                if (owner is not Track)
                    delta = Math.Min(delta, Math.Max(0, owner.DurSec - (start + dur)));
            }

            _after.Clear();
            foreach (var (ownerId, effectId) in _items)
            {
                if (!_before.TryGetValue(effectId, out var state) || Find(ownerId, effectId) is not { } fx)
                    continue;
                fx.StartSec = (state.StartSec ?? 0) + delta;
                fx.DurSec = state.DurSec;
                fx.Keyframes = state.Keyframes.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(p => p with { TimeSec = Math.Max(0, p.TimeSec + delta) }).ToList());
                _after[effectId] = EffectState.Capture(fx);
            }
        }

        private EffectInstance? Find(string ownerId, string effectId)
            => _editor.FindEffectOwner(ownerId)?.Effects.FirstOrDefault(e => e.Id == effectId);
    }
}
