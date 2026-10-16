using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Everything that differs between kinds of selectable things, in one table: how a key finds its object,
    /// what the status bar says, and what Delete does. Callers look a group up here instead of testing
    /// which kind of thing is selected, so adding a kind of thing means adding a row.
    /// </summary>
    public sealed record GroupBehavior(
        string Noun,
        Func<TimelineEditor, SelectionKey, ISelectable?> Resolve,
        Func<IReadOnlyList<ISelectable>, string> Summary,
        Func<int, string> Hint,
        Func<TimelineEditor, IReadOnlyList<SelectionKey>, int>? Delete);

    public static class SelectionGroups
    {
        private static string Counted(IReadOnlyList<ISelectable> items, string noun)
            => items.Count == 1 ? $"1 {noun} selected" : $"{items.Count} {noun}s selected";

        private static readonly Dictionary<SelectionGroup, GroupBehavior> Table = new()
        {
            [SelectionGroup.Clip] = new GroupBehavior("clip",
                (editor, key) => editor.FindClipForView(key.Id),
                items => items.Count > 1 && items.OfType<Clip>().Select(c => c.LinkGroupId).Distinct().Count() == 1
                         && items.OfType<Clip>().First().LinkGroupId is not null
                    ? $"{items.Count} linked clips selected"
                    : Counted(items, "clip"),
                _ => "Drag an edge to trim · Ctrl-click adds or removes · drag empty space (or Shift-drag anywhere) to select a region",
                // lift only what is selected (and its linked partners); never ripple or touch unselected clips
                (editor, keys) =>
                {
                    editor.LiftSelected();
                    return keys.Count;
                }),

            [SelectionGroup.Track] = new GroupBehavior("track",
                (editor, key) => editor.Document.Tracks.FirstOrDefault(t => t.Id == key.Id),
                items => "Track selected",
                _ => "Drop clips on a track · Delete removes the track",
                (editor, keys) =>
                {
                    var removed = keys.Count(k => editor.RemoveTrack(k.Id));
                    editor.Selection.Clear();
                    return removed;
                }),

            [SelectionGroup.Marker] = new GroupBehavior("marker",
                (editor, key) => editor.FindMarker(key.Id)?.Marker,
                items => Counted(items, "marker"),
                _ => "Ctrl-click toggles markers · Shift-click selects a range · Delete removes selected markers",
                (editor, keys) => editor.DeleteMarkers(keys.Select(k => k.Id))),

            [SelectionGroup.Transition] = new GroupBehavior("transition",
                (editor, key) => editor.GetTransition(key.Id),
                items => "Transition selected",
                _ => "Drag the transition handle to change its length · Delete removes it",
                (editor, keys) =>
                {
                    var before = editor.Selection.SelectedTransitionKey;
                    editor.RemoveSelectedTransition();
                    return before is not null && editor.GetTransition(before) is null ? 1 : 0;
                }),

            [SelectionGroup.Effect] = new GroupBehavior("effect",
                (editor, key) => editor.ResolveEffectTarget(key),
                items => items.Count == 1 ? "Effect selected" : $"{items.Count} effects selected",
                count => count == 1
                    ? "Shift-click adds more · Ctrl-drag duplicates · Delete removes the effect"
                    : "Drag to move them together · Shift-click adds or removes · Delete removes them",
                (editor, keys) =>
                {
                    // every selected bar goes, as one undo step
                    var removed = editor.RemoveEffects(keys.Select(k => (k.OwnerId ?? "", k.Id)));
                    editor.Selection.Clear();
                    return removed;
                }),

            [SelectionGroup.Media] = new GroupBehavior("media file",
                (_, _) => null,
                items => "Media selected",
                _ => "Drag it onto the timeline, or right-click it for more",
                null),
        };

        public static GroupBehavior Of(SelectionGroup group) => Table[group];
    }
}
