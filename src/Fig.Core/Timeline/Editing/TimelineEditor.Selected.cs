using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>
        /// The live object behind a selection key, or null when it no longer exists (deleted since it was
        /// selected). How each group is stored is in <see cref="SelectionGroups"/>.
        /// </summary>
        public ISelectable? Resolve(SelectionKey key) => SelectionGroups.Of(key.Group).Resolve(this, key);

        internal EffectTarget? ResolveEffectTarget(SelectionKey key)
        {
            if (key.OwnerId is null || FindEffectOwner(key.OwnerId) is not { } owner)
                return null;
            return owner.Effects.FirstOrDefault(e => e.Id == key.Id) is { } effect ? new EffectTarget(owner, effect) : null;
        }

        /// <summary>
        /// Deletes what is selected, whatever it is: each kind of thing says how (see <see cref="SelectionGroups"/>).
        /// Returns what was removed (a count and the noun for the message), or null when nothing could be.
        /// </summary>
        public (int Count, string Noun)? DeleteSelection()
        {
            var keys = Selection.Items;
            if (keys.Count == 0)
                return null;
            var behavior = SelectionGroups.Of(keys[0].Group);
            if (behavior.Delete is null)
                return null;
            var count = behavior.Delete(this, keys);
            return count > 0 ? (count, behavior.Noun) : null;
        }

        /// <summary>What the status bar says about the selection, and a hint for what to do next.</summary>
        public (string Summary, string Hint) DescribeSelection()
        {
            var items = Selected;
            if (items.Count == 0)
                return ("Nothing selected", "Click a clip to select it · drag empty space to box-select · Space plays · M adds a marker");
            var behavior = SelectionGroups.Of(items[0].Key.Group);
            return (behavior.Summary(items), behavior.Hint(items.Count));
        }

        /// <summary>Everything selected that still exists, in the order it was picked.</summary>
        public IReadOnlyList<ISelectable> Selected => Selection.Items.Select(Resolve).OfType<ISelectable>().ToList();

        /// <summary>The selected items that are of a given type (for the few edits that only make sense on one).</summary>
        public IEnumerable<T> SelectedOf<T>() where T : class, ISelectable => Selected.OfType<T>();

        /// <summary>
        /// What every selected item can be edited as: the facets they all have. An empty selection has none.
        /// </summary>
        public Facet SharedFacets
        {
            get
            {
                var selected = Selected;
                if (selected.Count == 0)
                    return Facet.None;
                return selected.Select(s => s.Facets).Aggregate((a, b) => a & b);
            }
        }
    }
}
