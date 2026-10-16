using System;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// What a selected thing can be edited as. The inspector shows one tab per facet that every selected
    /// item has, so it never has to ask "is this a video clip or a marker?": each item says what it is
    /// made of. A new kind of thing (an image layer, a text layer) only declares its facets.
    /// </summary>
    [Flags]
    public enum Facet
    {
        None = 0,
        /// <summary>Opacity and crop.</summary>
        Picture = 1 << 0,
        /// <summary>Volume.</summary>
        Sound = 1 << 1,
        /// <summary>Fade in and out.</summary>
        Fades = 1 << 2,
        /// <summary>Speed.</summary>
        Retime = 1 << 3,
        /// <summary>An effect stack, and the parameters of its effects.</summary>
        Effects = 1 << 4,
        /// <summary>The media file behind it.</summary>
        Source = 1 << 5,
        /// <summary>Text and how it is set.</summary>
        Text = 1 << 6,
        /// <summary>A track's own settings.</summary>
        Track = 1 << 7,
        /// <summary>A marker's name, time and colour.</summary>
        Marker = 1 << 8,
        /// <summary>A transition across a cut.</summary>
        Transition = 1 << 9,
    }

    /// <summary>
    /// Items of different groups are never selected together: picking something from another group
    /// replaces the selection. (A clip and a marker would have no tab in common to edit.)
    /// </summary>
    public enum SelectionGroup
    {
        Clip,
        Track,
        Marker,
        Transition,
        Effect,
        /// <summary>A file in the media library (not on the timeline).</summary>
        Media,
    }

    /// <summary>Identifies one selectable thing. <see cref="OwnerId"/> is set for things that live on another (an effect on its clip or track).</summary>
    public readonly record struct SelectionKey(SelectionGroup Group, string Id, string? OwnerId = null);

    /// <summary>Anything the user can select on the timeline.</summary>
    public interface ISelectable
    {
        SelectionKey Key { get; }

        /// <summary>What it is called in the inspector's title ("Video clip", "Point marker").</summary>
        string DisplayName { get; }

        /// <summary>What can be edited about it.</summary>
        Facet Facets { get; }
    }

    public static class SelectableExtensions
    {
        /// <summary>
        /// The clip or track whose effect stack this item edits: the item itself when it carries effects,
        /// the carrier of an effect bar, or null when it has no stack.
        /// </summary>
        public static IEffectOwner? EffectOwner(this ISelectable item) => item switch
        {
            IEffectOwner owner => owner,
            EffectTarget target => target.Owner,
            _ => null,
        };
    }

    /// <summary>An effect bar on the timeline: an effect together with the clip or track that carries it.</summary>
    public sealed class EffectTarget : ISelectable
    {
        public EffectTarget(IEffectOwner owner, EffectInstance effect)
        {
            Owner = owner;
            Effect = effect;
        }

        public IEffectOwner Owner { get; }
        public EffectInstance Effect { get; }

        public SelectionKey Key => new(SelectionGroup.Effect, Effect.Id, Owner.Id);

        public string DisplayName => Fig.Core.Media.EffectCatalog.Find(Effect.TypeId)?.DisplayName ?? "Effect";

        public Facet Facets => Facet.Effects;
    }
}
