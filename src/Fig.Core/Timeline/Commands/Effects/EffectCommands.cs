using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>Adds an effect instance to a clip's stack.</summary>
    public sealed class AddEffectCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly EffectInstance _effect;
        private bool _added;

        public string Description => $"Add effect {_effect.TypeId}";

        public AddEffectCommand(TimelineEditor editor, string clipId, EffectInstance effect)
        {
            _editor = editor;
            _clipId = clipId;
            _effect = effect;
        }

        public void Execute()
        {
            var clip = _editor.FindEffectOwner(_clipId)
                ?? throw new InvalidOperationException($"Clip '{_clipId}' not found");
            if (clip.Effects.Any(e => e.Id == _effect.Id))
                return;
            _effect.Order = clip.Effects.Count == 0 ? 0 : clip.Effects.Max(e => e.Order) + 1;
            clip.Effects.Add(_effect);
            _added = true;
        }

        public void Undo()
        {
            if (!_added)
                return;
            var clip = _editor.FindEffectOwner(_clipId);
            if (clip is null)
                return;
            clip.Effects.RemoveAll(e => e.Id == _effect.Id);
        }

        public void Redo() => Execute();
    }

    /// <summary>Removes an effect from a clip by id.</summary>
    public sealed class RemoveEffectCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly string _effectId;
        private EffectInstance? _removed;
        private int _index;

        public string Description => "Remove effect";

        public RemoveEffectCommand(TimelineEditor editor, string clipId, string effectId)
        {
            _editor = editor;
            _clipId = clipId;
            _effectId = effectId;
        }

        public void Execute()
        {
            var clip = _editor.FindEffectOwner(_clipId)
                ?? throw new InvalidOperationException($"Clip '{_clipId}' not found");
            _index = clip.Effects.FindIndex(e => e.Id == _effectId);
            if (_index < 0)
                throw new InvalidOperationException($"Effect '{_effectId}' not found");
            _removed = clip.Effects[_index];
            clip.Effects.RemoveAt(_index);
        }

        public void Undo()
        {
            if (_removed is null)
                return;
            var clip = _editor.FindEffectOwner(_clipId);
            if (clip is null)
                return;
            var i = Math.Clamp(_index, 0, clip.Effects.Count);
            clip.Effects.Insert(i, _removed);
        }

        public void Redo() => Execute();
    }

    /// <summary>
    /// Applies a catalog transition across an abutting cut: transitionOut on left,
    /// transitionIn on right (same type/duration).
    /// </summary>
    public sealed class ApplyTransitionAtCutCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _outgoingClipId;
        private readonly string _incomingClipId;
        private readonly TransitionRef _ref;
        private TransitionRef? _oldOut;
        private TransitionRef? _oldIn;
        private bool _done;

        public string Description => $"Apply {_ref.TypeId} at cut";

        public ApplyTransitionAtCutCommand(
            TimelineEditor editor,
            string outgoingClipId,
            string incomingClipId,
            TransitionRef transition)
        {
            _editor = editor;
            _outgoingClipId = outgoingClipId;
            _incomingClipId = incomingClipId;
            _ref = transition.Clone();
        }

        public void Execute()
        {
            var left = _editor.FindClip(_outgoingClipId)
                ?? throw new InvalidOperationException($"Clip '{_outgoingClipId}' not found");
            var right = _editor.FindClip(_incomingClipId)
                ?? throw new InvalidOperationException($"Clip '{_incomingClipId}' not found");
            _oldOut = left.TransitionOut?.Clone();
            _oldIn = right.TransitionIn?.Clone();
            left.TransitionOut = _ref.Clone();
            right.TransitionIn = _ref.Clone();
            _done = true;
        }

        public void Undo()
        {
            if (!_done)
                return;
            var left = _editor.FindClip(_outgoingClipId);
            var right = _editor.FindClip(_incomingClipId);
            if (left is not null)
                left.TransitionOut = _oldOut?.Clone();
            if (right is not null)
                right.TransitionIn = _oldIn?.Clone();
        }

        public void Redo() => Execute();
    }

    /// <summary>Sets one typed parameter on an effect. Slider drags coalesce into one undo step.</summary>
    public sealed class SetEffectParamCommand : ICoalescingEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly string _effectId;
        private readonly string _key;
        private ParamValue _newValue;
        private ParamValue _oldValue;
        private bool _done;

        public string Description => $"Set effect param {_key}";
        public string CoalesceKey => $"{_clipId}|{_effectId}|{_key}";

        public SetEffectParamCommand(TimelineEditor editor, string clipId, string effectId, string key, ParamValue newValue)
        {
            _editor = editor;
            _clipId = clipId;
            _effectId = effectId;
            _key = key;
            _newValue = newValue;
        }

        public void Execute()
        {
            if (FindEffect() is not { } effect || !effect.Params.ContainsKey(_key))
                return;
            _oldValue = effect.Params[_key];
            effect.Params[_key] = _newValue;
            _done = true;
        }

        public void Undo()
        {
            if (!_done)
                return;
            var effect = FindEffect();
            if (effect is not null && effect.Params.ContainsKey(_key))
                effect.Params[_key] = _oldValue;
        }

        public void Redo()
        {
            var effect = FindEffect();
            if (effect is not null && effect.Params.ContainsKey(_key))
                effect.Params[_key] = _newValue;
        }

        public bool CanCoalesceWith(IEditCommand other)
            => other is SetEffectParamCommand o && o.CoalesceKey == CoalesceKey;

        public void CoalesceFrom(IEditCommand other)
        {
            if (other is SetEffectParamCommand o && o.CoalesceKey == CoalesceKey)
            {
                _newValue = o._newValue;
                Redo();
            }
        }

        private EffectInstance? FindEffect()
        {
            var clip = _editor.FindEffectOwner(_clipId);
            if (clip is null)
                return null;
            foreach (var effect in clip.Effects)
                if (effect.Id == _effectId)
                    return effect;
            return null;
        }
    }

    /// <summary>Flips an effect's enabled flag.</summary>
    public sealed class ToggleEffectCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly string _clipId;
        private readonly string _effectId;
        private bool _old;
        private bool _done;

        public string Description => "Toggle effect";

        public ToggleEffectCommand(TimelineEditor editor, string clipId, string effectId)
        {
            _editor = editor;
            _clipId = clipId;
            _effectId = effectId;
        }

        public void Execute()
        {
            if (FindEffect() is not { } effect)
                return;
            _old = effect.Enabled;
            effect.Enabled = !effect.Enabled;
            _done = true;
        }

        public void Undo()
        {
            if (!_done)
                return;
            if (FindEffect() is { } effect)
                effect.Enabled = _old;
        }

        public void Redo() => Execute();

        private EffectInstance? FindEffect()
        {
            var clip = _editor.FindEffectOwner(_clipId);
            if (clip is null)
                return null;
            foreach (var effect in clip.Effects)
                if (effect.Id == _effectId)
                    return effect;
            return null;
        }
    }



}
