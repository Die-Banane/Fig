using System;

namespace Fig.Core.Timeline
{
    /// <summary>Which of a clip's fades: its picture's, or its sound's. They differ only on a video clip playing its own sound.</summary>
    public enum FadeOf { Picture, Sound }

    /// <summary>
    /// Shared linear fade-in / fade-out envelope for any clip kind.
    /// Video uses it as an opacity multiplier; audio uses it as a volume multiplier. A video clip's own sound
    /// has fades of its own (<see cref="VideoClip.AudioFadeInSec"/>), so picture and sound fade independently.
    /// </summary>
    public static class ClipFade
    {
        /// <summary>The fade-in and fade-out of the clip's picture or sound.</summary>
        public static (double In, double Out) Get(Clip clip, FadeOf of) =>
            of == FadeOf.Sound && clip is VideoClip video
                ? (video.AudioFadeInSec, video.AudioFadeOutSec)
                : (clip.FadeInSec, clip.FadeOutSec);

        /// <summary>Writes the fade-in and fade-out of the clip's picture or sound, as given.</summary>
        public static void Set(Clip clip, FadeOf of, double fadeIn, double fadeOut)
        {
            if (of == FadeOf.Sound && clip is VideoClip video)
            {
                video.AudioFadeInSec = fadeIn;
                video.AudioFadeOutSec = fadeOut;
            }
            else
            {
                clip.FadeInSec = fadeIn;
                clip.FadeOutSec = fadeOut;
            }
        }

        /// <summary>
        /// Clamps fade durations so both are &gt;= 0 and fadeIn + fadeOut &lt;= durSec.
        /// When the sum exceeds duration, both are scaled proportionally.
        /// </summary>
        public static (double FadeIn, double FadeOut) Clamp(double fadeIn, double fadeOut, double durSec)
        {
            fadeIn = Math.Max(0, fadeIn);
            fadeOut = Math.Max(0, fadeOut);
            if (durSec <= 1e-9)
                return (0, 0);
            var sum = fadeIn + fadeOut;
            if (sum > durSec)
            {
                var scale = durSec / sum;
                fadeIn *= scale;
                fadeOut *= scale;
            }
            return (fadeIn, fadeOut);
        }

        /// <summary>
        /// Linear fade multiplier in [0,1] at local time <paramref name="localT"/> (0 at clip start).
        /// Kind-agnostic — callers multiply by Opacity or Volume as appropriate.
        /// </summary>
        public static double Envelope(double localT, double durSec, double fadeInSec, double fadeOutSec)
        {
            var fin = fadeInSec <= 1e-9 ? 1.0 : Math.Clamp(localT / fadeInSec, 0, 1);
            var fout = fadeOutSec <= 1e-9 ? 1.0 : Math.Clamp((durSec - localT) / fadeOutSec, 0, 1);
            return fin * fout;
        }

        /// <summary>Envelope for a clip at local time (does not include Opacity/Volume).</summary>
        public static double Envelope(Clip clip, double localT)
            => Envelope(localT, clip.DurSec, clip.FadeInSec, clip.FadeOutSec);

        /// <summary>Opacity × fade envelope (video / compositing). Opacity may be keyframed.</summary>
        public static double EffectiveOpacity(Clip clip, double localT)
        {
            var opacity = ClipProperties.Opacity.NumberAt(clip, localT);
            return Math.Clamp(opacity, 0, 1) * Envelope(clip, localT);
        }

        /// <summary>Volume × the sound's fade envelope (audio mixing). Volume may be keyframed.</summary>
        public static double EffectiveVolume(Clip clip, double localT)
        {
            var volume = ClipProperties.Volume.NumberAt(clip, localT);
            var (fadeIn, fadeOut) = Get(clip, FadeOf.Sound);
            return Math.Clamp(volume, 0, 1) * Envelope(localT, clip.DurSec, fadeIn, fadeOut);
        }

        /// <summary>Writes clamped fade durations onto the clip.</summary>
        public static void Apply(Clip clip, double fadeInSec, double fadeOutSec)
        {
            var (i, o) = Clamp(fadeInSec, fadeOutSec, clip.DurSec);
            clip.FadeInSec = i;
            clip.FadeOutSec = o;
        }

        /// <summary>Sets fade-in without shrinking an existing fade-out (caps against remaining room).</summary>
        public static void ApplyFadeIn(Clip clip, double fadeInSec, FadeOf of = FadeOf.Picture)
        {
            var (_, fadeOut) = Get(clip, of);
            var max = Math.Max(0, clip.DurSec - Math.Max(0, fadeOut));
            Set(clip, of, Math.Min(Math.Max(0, fadeInSec), max), fadeOut);
        }

        /// <summary>Sets fade-out without shrinking an existing fade-in (caps against remaining room).</summary>
        public static void ApplyFadeOut(Clip clip, double fadeOutSec, FadeOf of = FadeOf.Picture)
        {
            var (fadeIn, _) = Get(clip, of);
            var max = Math.Max(0, clip.DurSec - Math.Max(0, fadeIn));
            Set(clip, of, fadeIn, Math.Min(Math.Max(0, fadeOutSec), max));
        }

        /// <summary>The fades a clip has: a video clip's picture and sound, or one set for any other clip.</summary>
        public static FadeOf[] Channels(Clip clip) => clip is VideoClip ? [FadeOf.Picture, FadeOf.Sound] : [FadeOf.Picture];

        /// <summary>
        /// After a split, the left half keeps fade-in and clears fade-out (then clamps).
        /// </summary>
        public static void ApplySplitLeft(Clip left)
        {
            foreach (var of in Channels(left))
            {
                var (fadeIn, _) = Get(left, of);
                Set(left, of, fadeIn, 0);
                ApplyFadeIn(left, fadeIn, of);
            }
        }

        /// <summary>
        /// After a split, the right half keeps fade-out and clears fade-in (then clamps).
        /// </summary>
        public static void ApplySplitRight(Clip right)
        {
            foreach (var of in Channels(right))
            {
                var (_, fadeOut) = Get(right, of);
                Set(right, of, 0, fadeOut);
                ApplyFadeOut(right, fadeOut, of);
            }
        }
    }
}
