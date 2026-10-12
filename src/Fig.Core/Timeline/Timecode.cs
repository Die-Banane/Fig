using System;
using System.Globalization;

namespace Fig.Core.Timeline
{
    /// <summary>Formats and parses non-drop timecode (HH:MM:SS:FF) against a frame rate.</summary>
    public static class Timecode
    {
        public static string Format(double seconds, FrameRate rate)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                seconds = 0;
            var fps = Math.Max(1, (int)Math.Round(rate.Fps));
            var totalFrames = (long)Math.Round(seconds * rate.Fps);
            var frames = totalFrames % fps;
            var totalSeconds = totalFrames / fps;
            return string.Create(CultureInfo.InvariantCulture,
                $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}:{frames:00}");
        }

        /// <summary>
        /// Accepts "HH:MM:SS:FF", "MM:SS:FF" (the last field is always frames), "SS:FF",
        /// or plain seconds such as "4" or "4.5".
        /// </summary>
        public static bool TryParse(string? text, FrameRate rate, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();

            if (!text.Contains(':'))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 0)
                    return false;
                return true;
            }

            var parts = text.Split(':');
            if (parts.Length is < 2 or > 4)
                return false;
            var values = new long[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                    return false;

            var fps = Math.Max(1, (int)Math.Round(rate.Fps));
            var frames = values[^1];
            long secs = 0;
            long unit = 1;
            for (var i = values.Length - 2; i >= 0; i--)
            {
                secs += values[i] * unit;
                unit *= 60;
            }
            seconds = secs + frames / (double)fps;
            return true;
        }
    }
}
