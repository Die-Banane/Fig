using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public class CommandHistory
    {
        private readonly Stack<IEditCommand> _undo = new();
        private readonly Stack<IEditCommand> _redo = new();

        public int MaxDepth { get; }

        public CommandHistory(int maxDepth = 100)
        {
            MaxDepth = maxDepth;
        }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        // while a Group is open: how deep, and where its steps begin on the undo stack
        private int _groupDepth;
        private int _groupStart;

        /// <summary>
        /// Everything done until the returned handle is disposed becomes one undo step (deleting a mixed selection:
        /// clips, effect bars and markers come back together). Groups nest; the outermost one makes the step.
        /// </summary>
        public IDisposable Group()
        {
            if (_groupDepth++ == 0)
                _groupStart = _undo.Count;
            return new GroupScope(this);
        }

        private void EndGroup()
        {
            if (--_groupDepth > 0)
                return;
            var count = _undo.Count - _groupStart;
            if (count <= 1)
                return;
            var steps = new IEditCommand[count];
            for (var i = count - 1; i >= 0; i--)
                steps[i] = _undo.Pop();     // popped newest first, kept in the order they ran
            _undo.Push(new CompositeCommand(steps));
        }

        private sealed class GroupScope(CommandHistory history) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done)
                    return;
                _done = true;
                history.EndGroup();
            }
        }

        // set by BreakCoalescing: the next coalescing edit starts a step of its own
        private bool _sealed;

        /// <summary>Ends the current coalescing run, so the next edit is its own undo step (each gizmo drag is one).</summary>
        public void BreakCoalescing() => _sealed = true;

        public void Execute(IEditCommand command)
        {
            _sealed = false;
            command.Execute();
            _undo.Push(command);
            _redo.Clear();
            TrimToMaxDepth();
        }

        /// <summary>
        /// Like <see cref="Execute"/>, but if the top undo entry can coalesce with
        /// <paramref name="command"/>, merges into it instead of pushing a new step.
        /// Used for scrubbing continuous properties (opacity, crop, volume).
        /// </summary>
        public void ExecuteCoalescing(ICoalescingEditCommand command)
        {
            // inside a group, never merge into a step from before it
            if (!_sealed && _undo.Count > (_groupDepth > 0 ? _groupStart : 0) && _undo.Peek() is ICoalescingEditCommand prev && prev.CanCoalesceWith(command))
            {
                prev.CoalesceFrom(command);
                _redo.Clear();
                return;
            }

            Execute(command);
        }

        private void TrimToMaxDepth()
        {
            if (_undo.Count <= MaxDepth)
                return;
            // drop oldest: rebuild without the bottom entry
            var keep = _undo.ToArray(); // top-first
            _undo.Clear();
            for (var i = Math.Min(keep.Length, MaxDepth) - 1; i >= 0; i--)
                _undo.Push(keep[i]);
        }

        public bool Undo()
        {
            if (_undo.Count == 0)
                return false;
            var command = _undo.Pop();
            command.Undo();
            _redo.Push(command);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0)
                return false;
            var command = _redo.Pop();
            command.Redo();
            _undo.Push(command);
            return true;
        }
    }
}
