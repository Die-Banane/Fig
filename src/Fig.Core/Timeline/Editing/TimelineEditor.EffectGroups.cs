using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>Removes several effect bars as one undo step. Returns how many were removed.</summary>
        public int RemoveEffects(IEnumerable<(string OwnerId, string EffectId)> items)
        {
            var commands = items.Distinct()
                .Where(i => FindEffectOwner(i.OwnerId)?.Effects.Any(e => e.Id == i.EffectId) == true)
                .Select(i => (IEditCommand)new RemoveEffectCommand(this, i.OwnerId, i.EffectId))
                .ToArray();
            if (commands.Length == 0)
                return 0;
            History.Execute(new CompositeCommand(commands));
            RaiseChanged();
            return commands.Length;
        }

        /// <summary>
        /// Moves several effect bars together by <paramref name="deltaSec"/> (the total from where the drag began).
        /// Pass the same <paramref name="gesture"/> for every step of one drag so it undoes in one go.
        /// </summary>
        public void MoveEffects(IReadOnlyList<(string OwnerId, string EffectId)> items, double deltaSec, string gesture)
        {
            if (items.Count == 0)
                return;
            History.ExecuteCoalescing(new MoveEffectsCommand(this, items, deltaSec, gesture));
            RaiseChanged();
        }
    }
}
