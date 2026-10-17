using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// The parameters an effect or a transition runs with, read through the schema its class declares: a missing value
    /// is the declared default, a number is held to the declared range, a choice to the declared choices. Declaring a
    /// parameter is the only place its default and range are written.
    /// </summary>
    public readonly struct ParamSet
    {
        private readonly IReadOnlyDictionary<string, ParamDef> _schema;
        private readonly IReadOnlyDictionary<string, ParamValue> _values;

        public ParamSet(IReadOnlyDictionary<string, ParamDef> schema, IReadOnlyDictionary<string, ParamValue> values)
        {
            _schema = schema;
            _values = values;
        }

        /// <summary>Whether the class declares <paramref name="key"/> (for code shared by several that declare different ones).</summary>
        public bool Declares(string key) => _schema.ContainsKey(key);

        public double Number(string key)
        {
            var def = Def(key);
            var value = _values.TryGetValue(key, out var v) && v.Kind is ParamKind.Double or ParamKind.Int ? v.AsNumber : def.Default;
            if (!double.IsFinite(value))
                value = def.Default;
            return def.Max > def.Min ? Math.Clamp(value, def.Min, def.Max) : value;
        }

        public int Int(string key) => (int)Math.Round(Number(key));

        public bool Bool(string key)
            => _values.TryGetValue(key, out var v) && v.Kind == ParamKind.Bool ? v.AsBool : Def(key).Default != 0;

        public uint Color(string key)
            => _values.TryGetValue(key, out var v) && v.Kind == ParamKind.Color ? v.AsColor : (uint)Def(key).Default;

        /// <summary>The index of the picked choice.</summary>
        public int Choice(string key)
        {
            var def = Def(key);
            var index = _values.TryGetValue(key, out var v) && v.Kind == ParamKind.List ? v.AsChoice : (int)Math.Round(def.Default);
            return def.Choices.Count > 0 ? Math.Clamp(index, 0, def.Choices.Count - 1) : index;
        }

        private ParamDef Def(string key)
            => _schema.TryGetValue(key, out var def) ? def
                : throw new InvalidOperationException($"Parameter '{key}' is read but not declared");
    }

    /// <summary>The parameters a class declares, by key (read once per class).</summary>
    public static class ParamSchema
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, ParamDef>> Cache = new();

        public static IReadOnlyList<ParamDef> Read<TAttribute>(Type type) where TAttribute : ParamAttribute
            => type.GetCustomAttributes<TAttribute>().Select(a => a.ToDef()).ToList();

        public static IReadOnlyDictionary<string, ParamDef> Of<TAttribute>(Type type) where TAttribute : ParamAttribute
            => Cache.GetOrAdd(type, t => Read<TAttribute>(t).ToDictionary(d => d.Key));
    }

    /// <summary>
    /// The base of a built-in effect: declare it with <see cref="EffectAttribute"/> and its parameters with
    /// <see cref="EffectParamAttribute"/>, then write <see cref="Render"/>, reading the parameters from the
    /// <see cref="ParamSet"/> it is given.
    /// </summary>
    internal abstract class EffectProcessor : IEffectProcessor
    {
        private readonly IReadOnlyDictionary<string, ParamDef> _schema;

        protected EffectProcessor() => _schema = ParamSchema.Of<EffectParamAttribute>(GetType());

        public DecodedFrame Apply(DecodedFrame frame, IReadOnlyDictionary<string, ParamValue> parameters, double localT)
            => Render(frame, new ParamSet(_schema, parameters), localT);

        /// <summary>Returns the frame changed (a new one, or <paramref name="frame"/> itself when there is nothing to do).</summary>
        protected abstract DecodedFrame Render(DecodedFrame frame, ParamSet p, double localT);
    }

    /// <summary>
    /// The base of a built-in transition: declare it with <see cref="TransitionAttribute"/> and its parameters with
    /// <see cref="TransitionParamAttribute"/>, then write <see cref="Render"/>. It is only called for two frames of
    /// the same size (frames that differ cut at the midpoint), with <c>t</c> in 0..1.
    /// </summary>
    internal abstract class TransitionBlender : ITransitionBlender
    {
        private readonly IReadOnlyDictionary<string, ParamDef> _schema;

        protected TransitionBlender() => _schema = ParamSchema.Of<TransitionParamAttribute>(GetType());

        public DecodedFrame Blend(DecodedFrame outgoing, DecodedFrame incoming, double t01, IReadOnlyDictionary<string, ParamValue> parameters)
        {
            var t = double.IsFinite(t01) ? Math.Clamp(t01, 0, 1) : 0;
            if (outgoing.Width != incoming.Width || outgoing.Height != incoming.Height)
                return t < 0.5 ? outgoing : incoming;
            return Render(outgoing, incoming, t, new ParamSet(_schema, parameters));
        }

        protected abstract DecodedFrame Render(DecodedFrame outgoing, DecodedFrame incoming, double t, ParamSet p);
    }
}
