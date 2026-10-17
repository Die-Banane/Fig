using System;
using System.Collections.Generic;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// A resolved transition across an abutting cut. The underlying data lives on the
    /// clips as <see cref="Clip.TransitionOut"/> (left) and <see cref="Clip.TransitionIn"/>
    /// (right); the effective duration is the max of the two edges (matching the engine).
    /// </summary>
    public sealed record CutTransition(
        string LeftClipId,
        string RightClipId,
        Clip Left,
        Clip Right,
        string TypeId,
        double DurationSec,
        double CutSec) : ISelectable
    {
        /// <summary>The identity used to select and look this transition up: its two clips.</summary>
        public string Key => $"{LeftClipId}|{RightClipId}";

        SelectionKey ISelectable.Key => new(SelectionGroup.Transition, Key);
        public string DisplayName => "Transition";
        public Facet Facets => Facet.Transition;
    }

    /// <summary>
    /// An edit to the transition across one cut. It lives on two clip edges (the left clip's out, the right clip's in,
    /// either of which may be missing); this snapshots both, makes the change, and puts the snapshot back on undo, so a
    /// new kind of transition edit only says what it changes.
    /// </summary>
    public abstract class CutTransitionEdit : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private TransitionRef? _oldOut;
        private TransitionRef? _oldIn;
        private bool _done;

        protected CutTransitionEdit(TimelineEditor editor, string leftClipId, string rightClipId)
        {
            _editor = editor;
            LeftClipId = leftClipId;
            RightClipId = rightClipId;
        }

        protected string LeftClipId { get; }
        protected string RightClipId { get; }

        public abstract string Description { get; }

        /// <summary>Makes the change to the cut's two clips.</summary>
        protected abstract void Change(Clip left, Clip right);

        /// <summary>Whether there is anything to change: by default, a transition on the cut.</summary>
        protected virtual bool Applies(Clip left, Clip right) => left.TransitionOut is not null || right.TransitionIn is not null;

        /// <summary>Runs <paramref name="change"/> on each edge of the transition the two clips have.</summary>
        protected static void EachEdge(Clip left, Clip right, Action<TransitionRef> change)
        {
            if (left.TransitionOut is not null)
                change(left.TransitionOut);
            if (right.TransitionIn is not null)
                change(right.TransitionIn);
        }

        public void Execute()
        {
            if (Find() is not var (left, right) || !Applies(left, right))
                return;
            _oldOut = left.TransitionOut?.Clone();
            _oldIn = right.TransitionIn?.Clone();
            Change(left, right);
            _done = true;
        }

        public void Undo()
        {
            if (!_done || Find() is not var (left, right))
                return;
            left.TransitionOut = _oldOut?.Clone();
            right.TransitionIn = _oldIn?.Clone();
        }

        public void Redo()
        {
            if (_done && Find() is var (left, right))
                Change(left, right);
        }

        private (Clip Left, Clip Right)? Find()
            => _editor.FindClip(LeftClipId) is { } left && _editor.FindClip(RightClipId) is { } right ? (left, right) : null;
    }

    /// <summary>Puts a catalog transition across an abutting cut: the same one on both clip edges.</summary>
    public sealed class ApplyTransitionAtCutCommand(TimelineEditor editor, string outgoingClipId, string incomingClipId, TransitionRef transition)
        : CutTransitionEdit(editor, outgoingClipId, incomingClipId)
    {
        private readonly TransitionRef _transition = transition.Clone();

        public override string Description => $"Apply {_transition.TypeId} at cut";

        protected override bool Applies(Clip left, Clip right) => true;

        protected override void Change(Clip left, Clip right)
        {
            left.TransitionOut = _transition.Clone();
            right.TransitionIn = _transition.Clone();
        }
    }

    /// <summary>Removes the transition across an abutting cut (clears both clip edges).</summary>
    public sealed class RemoveTransitionCommand(TimelineEditor editor, string leftClipId, string rightClipId)
        : CutTransitionEdit(editor, leftClipId, rightClipId)
    {
        public override string Description => "Remove transition";

        protected override void Change(Clip left, Clip right)
        {
            left.TransitionOut = null;
            right.TransitionIn = null;
        }
    }

    /// <summary>Sets a typed parameter on a cut transition (writes both clip edges). Coalesces.</summary>
    public sealed class SetTransitionParamCommand(TimelineEditor editor, string leftClipId, string rightClipId, string key, ParamValue value)
        : CutTransitionEdit(editor, leftClipId, rightClipId), ICoalescingEditCommand
    {
        private ParamValue _value = value;

        public override string Description => $"Set transition param {key}";
        private string CoalesceKey => $"{LeftClipId}|{RightClipId}|{key}";

        protected override void Change(Clip left, Clip right) => EachEdge(left, right, edge => edge.Params[key] = _value);

        public bool CanCoalesceWith(IEditCommand other) => other is SetTransitionParamCommand o && o.CoalesceKey == CoalesceKey;

        public void CoalesceFrom(IEditCommand other)
        {
            _value = ((SetTransitionParamCommand)other)._value;
            Redo();
        }
    }

    /// <summary>Resizes a cut transition by writing the same duration onto both clip edges. Coalesces.</summary>
    public sealed class SetTransitionDurationCommand(TimelineEditor editor, string leftClipId, string rightClipId, double durationSec)
        : CutTransitionEdit(editor, leftClipId, rightClipId), ICoalescingEditCommand
    {
        private double _durationSec = Math.Max(0, durationSec);

        public override string Description => "Resize transition";
        private string CoalesceKey => $"{LeftClipId}|{RightClipId}";

        protected override void Change(Clip left, Clip right) => EachEdge(left, right, edge => edge.DurationSec = _durationSec);

        public bool CanCoalesceWith(IEditCommand other) => other is SetTransitionDurationCommand o && o.CoalesceKey == CoalesceKey;

        public void CoalesceFrom(IEditCommand other)
        {
            _durationSec = ((SetTransitionDurationCommand)other)._durationSec;
            Redo();
        }
    }
}
