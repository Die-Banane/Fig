using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// The built-in effect catalog, discovered from every <see cref="IEffectProcessor"/>
    /// implementation in this assembly that carries an <see cref="EffectAttribute"/>.
    /// Adding an effect is one step — write the class with its descriptor and it is picked
    /// up here automatically: metadata for the UI (<see cref="All"/> / <see cref="Find"/>)
    /// and the processor for rendering (<see cref="Resolve"/>).
    /// </summary>
    public static class EffectCatalog
    {
        public const string Brightness = "brightness";
        public const string Grayscale = "grayscale";
        public const string Tint = "tint";
        public const string Contrast = "contrast";
        public const string Saturation = "saturation";
        public const string Hue = "hue";
        public const string Invert = "invert";
        public const string Sepia = "sepia";
        public const string Vignette = "vignette";
        public const string Sharpen = "sharpen";
        public const string Pixelate = "pixelate";
        public const string Flip = "flip";
        public const string Posterize = "posterize";
        public const string CropWithPadding = "cropWithPadding";
        public const string Transform = "transform";
        public const string ChromaticAberration = "chromaticAberration";

        private sealed record EffectEntry(Type Implementation, EffectCatalogEntry Metadata);

        private static readonly Lazy<IReadOnlyList<EffectEntry>> Entries = new(Discover);

        private static readonly Lazy<IReadOnlyList<EffectCatalogEntry>> Catalog =
            new(() => Entries.Value.Select(e => e.Metadata).ToList());

        private static readonly Lazy<Dictionary<string, IEffectProcessor>> Processors = new(() =>
        {
            var map = new Dictionary<string, IEffectProcessor>();
            foreach (var entry in Entries.Value)
                map[entry.Metadata.TypeId] = (IEffectProcessor)Activator.CreateInstance(entry.Implementation)!;
            return map;
        });

        // Effects that are not discovered: user graph effects. Each owner (a library) replaces its own set.
        private sealed record UserEffect(EffectCatalogEntry Entry, IEffectProcessor Processor);

        private static readonly object UserLock = new();
        private static readonly Dictionary<object, IReadOnlyList<UserEffect>> UserSources = new(ReferenceEqualityComparer.Instance);
        private static IReadOnlyList<UserEffect> _userEffects = Array.Empty<UserEffect>();
        private static IReadOnlyList<EffectCatalogEntry>? _combined;

        /// <summary>Built-in effects, followed by user effects (graph effects) from every registered library.</summary>
        public static IReadOnlyList<EffectCatalogEntry> All
        {
            get
            {
                lock (UserLock)
                {
                    if (_userEffects.Count == 0)
                        return Catalog.Value;
                    return _combined ??= Catalog.Value.Concat(_userEffects.Select(u => u.Entry)).ToList();
                }
            }
        }

        public static EffectCatalogEntry? Find(string typeId)
        {
            foreach (var entry in All)
                if (entry.TypeId == typeId)
                    return entry;
            return null;
        }

        public static IEffectProcessor? Resolve(string typeId)
        {
            if (Processors.Value.TryGetValue(typeId, out var processor))
                return processor;
            lock (UserLock)
            {
                foreach (var user in _userEffects)
                    if (user.Entry.TypeId == typeId)
                        return user.Processor;
            }
            return null;
        }

        /// <summary>Publishes (or replaces) the user effects owned by <paramref name="owner"/>.</summary>
        public static void SetUserEffects(object owner, IEnumerable<(EffectCatalogEntry Entry, IEffectProcessor Processor)> effects)
        {
            lock (UserLock)
            {
                UserSources[owner] = effects.Select(e => new UserEffect(e.Entry, e.Processor)).ToList();
                _userEffects = UserSources.Values.SelectMany(v => v).ToList();
                _combined = null;
            }
        }

        public static void ClearUserEffects(object owner)
        {
            lock (UserLock)
            {
                if (UserSources.Remove(owner))
                {
                    _userEffects = UserSources.Values.SelectMany(v => v).ToList();
                    _combined = null;
                }
            }
        }

        private static IReadOnlyList<EffectEntry> Discover()
        {
            var list = new List<EffectEntry>();
            var assembly = typeof(IEffectProcessor).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IEffectProcessor).IsAssignableFrom(type))
                    continue;
                var descriptor = type.GetCustomAttribute<EffectAttribute>();
                if (descriptor is null)
                    continue;

                var entry = new EffectCatalogEntry
                {
                    TypeId = descriptor.TypeId,
                    Category = descriptor.Category,
                    Kind = descriptor.Kind,
                    DisplayName = descriptor.DisplayName,
                    Icon = descriptor.Icon,
                    Description = descriptor.Description,
                    ParamSchema = ParamSchema.Read<EffectParamAttribute>(type),
                };
                list.Add(new EffectEntry(type, entry));
            }
            return list.OrderBy(e => e.Metadata.TypeId).ToList();
        }
    }
}
