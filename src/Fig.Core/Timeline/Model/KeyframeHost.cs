using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Names a set of keyframes, wherever they live. A clip's own keys (<see cref="EffectId"/> and <see cref="Lane"/>
    /// null: opacity, volume, crop, text size...), an effect's keys on its track or clip (<see cref="EffectId"/>),
    /// or a clip's speed curve (<see cref="Lane"/> = <see cref="SpeedLane"/>, drawn as its own line). Every keyframe
    /// operation takes one of these, so every kind of keyframe behaves the same.
    /// </summary>
    public readonly record struct KeyframeHostRef(string OwnerId, string? EffectId = null, string? Lane = null)
    {
        public const string SpeedLane = "speed";

        public static KeyframeHostRef OfClip(string clipId) => new(clipId);
        public static KeyframeHostRef OfEffect(string ownerId, string effectId) => new(ownerId, effectId);
        public static KeyframeHostRef OfSpeed(string clipId) => new(clipId, null, SpeedLane);
    }

    /// <summary>One diamond: every key a host has at one time (seconds from the host's start).</summary>
    public readonly record struct KeyframeRef(KeyframeHostRef Host, double TimeSec);

    /// <summary>
    /// A set of keyframes and what is different about where it lives: where its time 0 is on the timeline, how far
    /// its keys may go, what a key's value may be, how new keys ease, and what has to happen after a change (a speed
    /// curve re-syncs its clip's out point). Made by <see cref="TimelineEditor.FindKeyframeHost"/>.
    /// </summary>
    public sealed class KeyframeHost
    {
        internal KeyframeHost(KeyframeHostRef reference, Dictionary<string, List<KeyframePoint>> store, Func<string, bool> includes,
            double originSec, double limitSec, KeyframeEasing newKeyEasing,
            Func<string, double, ParamValue> toValue, Func<double, IReadOnlyDictionary<string, double>> currentValues,
            Action changed)
        {
            Ref = reference;
            Store = store;
            _includes = includes;
            OriginSec = originSec;
            LimitSec = limitSec;
            NewKeyEasing = newKeyEasing;
            _toValue = toValue;
            _currentValues = currentValues;
            _changed = changed;
        }

        private readonly Func<string, bool> _includes;
        private readonly Func<string, double, ParamValue> _toValue;
        private readonly Func<double, IReadOnlyDictionary<string, double>> _currentValues;
        private readonly Action _changed;

        public KeyframeHostRef Ref { get; }

        /// <summary>The dictionary the keys live in (a clip's or an effect's); a clip's holds both lanes.</summary>
        internal Dictionary<string, List<KeyframePoint>> Store { get; }

        /// <summary>Where this host's time 0 is on the timeline: its clip's start, or 0 for a track effect.</summary>
        public double OriginSec { get; }

        /// <summary>How far its keys may go: its clip's length, or no limit for a track effect.</summary>
        public double LimitSec { get; }

        /// <summary>How a key added with the current look eases into the next.</summary>
        public KeyframeEasing NewKeyEasing { get; }

        /// <summary>The keyframe lists that belong to this host, by name.</summary>
        public IEnumerable<KeyValuePair<string, List<KeyframePoint>>> Tracks => Store.Where(kv => _includes(kv.Key));

        public bool Includes(string key) => _includes(key);

        /// <summary>Every time with a key, in order: one per diamond.</summary>
        public IReadOnlyList<double> Times
            => Tracks.SelectMany(kv => kv.Value.Select(k => Math.Round(k.TimeSec, 6))).Distinct().OrderBy(t => t).ToList();

        public bool HasKeyAt(double timeSec) => Tracks.Any(kv => kv.Value.Any(k => Math.Abs(k.TimeSec - timeSec) < 1e-3));

        /// <summary>The easing of the keys at a time (the first one there), or null when there are none.</summary>
        public KeyframeEasing? EasingAt(double timeSec)
            => Tracks.SelectMany(kv => kv.Value).Where(k => Math.Abs(k.TimeSec - timeSec) < 1e-3)
                .Select(k => (KeyframeEasing?)k.Easing).FirstOrDefault();

        /// <summary>A key's value as stored: held to what it may be (a speed to 0.1–8×, an integer parameter rounded).</summary>
        public ParamValue ToValue(string key, double value) => _toValue(key, value);

        /// <summary>What every keyable property of the host is at a moment: the values a new "current look" key holds.</summary>
        public IReadOnlyDictionary<string, double> CurrentValues(double timeSec) => _currentValues(timeSec);

        internal void Changed() => _changed();

        /// <summary>Drops the host's empty lists, so "has keyframes" stays true only while there are some.</summary>
        internal void Prune()
        {
            foreach (var key in Store.Keys.Where(k => _includes(k) && Store[k].Count == 0).ToList())
                Store.Remove(key);
        }
    }
}
