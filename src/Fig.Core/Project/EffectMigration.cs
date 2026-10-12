using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Project
{
    /// <summary>Upgrades effects saved by earlier versions to their current form when a project loads.</summary>
    public static class EffectMigration
    {
        /// <summary>The short-lived "Zoom region" effect, now a Transform with two keyframes.</summary>
        public const string LegacyZoomRegion = "zoomRegion";

        /// <summary>Migrates every clip in the project. Returns how many effects were changed.</summary>
        public static int Migrate(Project project)
        {
            var changed = 0;
            foreach (var clip in project.Timelines.SelectMany(t => t.Tracks).SelectMany(t => t.Clips))
                foreach (var effect in clip.Effects)
                {
                    if (effect.TypeId == LegacyZoomRegion)
                    {
                        ConvertZoomRegion(clip, effect);
                        changed++;
                    }

                    // An animated Transform covers the whole clip (its keyframes hold before and after), so an
                    // earlier time-limiting bar would make it snap back. Drop the limit.
                    if (effect.TypeId == EffectCatalog.Transform && effect.Keyframes.Count > 0 && effect.HasRange)
                    {
                        effect.StartSec = null;
                        effect.DurSec = null;
                        changed++;
                    }
                }
            return changed;
        }

        private static void ConvertZoomRegion(Clip clip, EffectInstance fx)
        {
            double Num(string key, double fallback) => fx.Params.TryGetValue(key, out var v) ? v.AsNumber : fallback;
            var easing = fx.Params.TryGetValue("ease", out var e)
                ? e.AsChoice switch
                {
                    1 => KeyframeEasing.EaseIn,
                    2 => KeyframeEasing.EaseOut,
                    0 => KeyframeEasing.Linear,
                    _ => KeyframeEasing.EaseInOut,
                }
                : KeyframeEasing.EaseInOut;

            var start = fx.StartSec ?? 0;
            var dur = fx.DurSec ?? clip.DurSec;
            var startFrame = (X: Num("startX", 0.5), Y: Num("startY", 0.5), Zoom: Num("startZoom", 1));
            var endFrame = (X: Num("endX", 0.75), Y: Num("endY", 0.75), Zoom: Num("endZoom", 2.5));

            fx.TypeId = EffectCatalog.Transform;
            fx.StartSec = null;
            fx.DurSec = null;
            fx.Params = new Dictionary<string, ParamValue>
            {
                ["centerX"] = ParamValue.OfDouble(startFrame.X),
                ["centerY"] = ParamValue.OfDouble(startFrame.Y),
                ["zoom"] = ParamValue.OfDouble(startFrame.Zoom),
                ["rotation"] = ParamValue.OfDouble(0),
            };
            fx.Keyframes = new Dictionary<string, List<KeyframePoint>>
            {
                ["centerX"] = Track(start, startFrame.X, dur, endFrame.X, easing),
                ["centerY"] = Track(start, startFrame.Y, dur, endFrame.Y, easing),
                ["zoom"] = Track(start, startFrame.Zoom, dur, endFrame.Zoom, easing),
                ["rotation"] = Track(start, 0, dur, 0, easing),
            };
        }

        private static List<KeyframePoint> Track(double start, double from, double dur, double to, KeyframeEasing easing) => new()
        {
            new KeyframePoint(start, ParamValue.OfDouble(from), easing),
            new KeyframePoint(start + dur, ParamValue.OfDouble(to), KeyframeEasing.EaseInOut),
        };
    }
}
