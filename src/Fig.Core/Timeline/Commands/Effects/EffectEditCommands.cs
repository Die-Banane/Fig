using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>The parts of an effect that editing keyframes and its bar can change, for undo.</summary>
    internal sealed record EffectState(
        double? StartSec,
        double? DurSec,
        Dictionary<string, List<KeyframePoint>> Keyframes,
        Dictionary<string, ParamValue> Params)
    {
        public static EffectState Capture(EffectInstance fx) => new(
            fx.StartSec,
            fx.DurSec,
            fx.Keyframes.ToDictionary(kv => kv.Key, kv => new List<KeyframePoint>(kv.Value)),
            new Dictionary<string, ParamValue>(fx.Params));

        public void RestoreTo(EffectInstance fx)
        {
            fx.StartSec = StartSec;
            fx.DurSec = DurSec;
            fx.Keyframes = Keyframes.ToDictionary(kv => kv.Key, kv => new List<KeyframePoint>(kv.Value));
            fx.Params = new Dictionary<string, ParamValue>(Params);
        }
    }

    /// <summary>
    /// Applies a change to one effect (its bar, keyframes or parameters) and undoes it by restoring a
    /// snapshot. Commands with the same <c>coalesceKey</c> (a drag) merge into a single undo step.
    /// </summary>
    public sealed class EffectEditCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly string _effectId;
        private readonly string? _coalesceKey;
        private readonly Action<EffectInstance> _mutate;
        private EffectState? _before;
        private EffectState? _after;

        public string Description { get; }

        public EffectEditCommand(TimelineEditor editor, string clipId, string effectId, string description,
            string? coalesceKey, Action<EffectInstance> mutate)
        {
            _editor = editor;
            _clipId = clipId;
            _effectId = effectId;
            Description = description;
            _coalesceKey = coalesceKey;
            _mutate = mutate;
        }

        public void Execute()
        {
            if (Find() is not { } fx)
                return;
            _before = EffectState.Capture(fx);
            _mutate(fx);
            _after = EffectState.Capture(fx);
        }

        public void Undo()
        {
            if (_before is not null && Find() is { } fx)
                _before.RestoreTo(fx);
        }

        public void Redo()
        {
            if (_after is not null && Find() is { } fx)
                _after.RestoreTo(fx);
        }

        public bool CanCoalesceWith(IEditCommand other)
            => _coalesceKey is not null
               && other is EffectEditCommand o
               && o._coalesceKey == _coalesceKey && o._clipId == _clipId && o._effectId == _effectId;

        public void CoalesceFrom(IEditCommand other)
        {
            // the history does not run a coalesced command, so apply its change here
            if (other is EffectEditCommand o && Find() is { } fx)
            {
                o._mutate(fx);
                _after = EffectState.Capture(fx);
            }
        }

        private EffectInstance? Find() => _editor.FindEffectOwner(_clipId)?.Effects.FirstOrDefault(e => e.Id == _effectId);
    }
}
