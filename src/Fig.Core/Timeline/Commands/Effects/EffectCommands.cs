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
}
