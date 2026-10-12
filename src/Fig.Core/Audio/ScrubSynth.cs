using System;

namespace Fig.Core.Audio
{
    /// <summary>
    /// Turns "the playhead moved from A to B" into audio, the way a jog wheel does: the sound is the timeline
    /// mix between the two times, resampled to fit the output, so it plays faster (and higher) as you scrub
    /// faster and runs backwards when you scrub backwards.
    /// </summary>
    public sealed class ScrubSynth
    {
        public const int SampleRate = AudioMixer.SampleRate;

        /// <summary>Soften block edges so starts and stops don't click.</summary>
        private const int FadeFrames = 144;   // 3 ms

        private const double WindowSec = 0.5;

        private readonly Func<double, double, float[]> _mix;
        private float[] _window = Array.Empty<float>();
        private double _windowStart;
        private double _windowEnd;

        /// <param name="mix">Returns interleaved stereo for (startSec, durationSec) of the timeline.</param>
        public ScrubSynth(Func<double, double, float[]> mix) => _mix = mix;

        /// <summary>Forgets the cached audio (after the timeline changes).</summary>
        public void Invalidate() => _windowEnd = _windowStart = 0;

        /// <summary>
        /// Interleaved stereo for <paramref name="outFrames"/> frames that sweep the timeline from
        /// <paramref name="fromSec"/> to <paramref name="toSec"/> (backwards when to &lt; from).
        /// </summary>
        public float[] Render(double fromSec, double toSec, int outFrames, bool fadeIn, bool fadeOut)
        {
            var output = new float[Math.Max(0, outFrames) * 2];
            if (outFrames <= 0)
                return output;

            var lo = Math.Max(0, Math.Min(fromSec, toSec));
            var hi = Math.Max(lo + 1.0 / SampleRate, Math.Max(fromSec, toSec));
            EnsureWindow(lo, hi, reverse: toSec < fromSec);

            var framesInWindow = _window.Length / 2;
            for (var i = 0; i < outFrames; i++)
            {
                var pos = fromSec + (toSec - fromSec) * (i / (double)outFrames);
                var f = (pos - _windowStart) * SampleRate;
                var i0 = (int)Math.Floor(f);
                var frac = (float)(f - i0);
                var a = Math.Clamp(i0, 0, framesInWindow - 1);
                var b = Math.Clamp(i0 + 1, 0, framesInWindow - 1);

                var gain = 1f;
                if (fadeIn && i < FadeFrames)
                    gain = i / (float)FadeFrames;
                if (fadeOut && outFrames - 1 - i < FadeFrames)
                    gain = Math.Min(gain, (outFrames - 1 - i) / (float)FadeFrames);

                output[i * 2] = (_window[a * 2] + (_window[b * 2] - _window[a * 2]) * frac) * gain;
                output[i * 2 + 1] = (_window[a * 2 + 1] + (_window[b * 2 + 1] - _window[a * 2 + 1]) * frac) * gain;
            }
            return output;
        }

        /// <summary>Mixes a half-second window around the sweep so most blocks need no new decode.</summary>
        private void EnsureWindow(double lo, double hi, bool reverse)
        {
            if (lo >= _windowStart && hi <= _windowEnd - 0.01 && _window.Length > 0)
                return;

            var length = Math.Max(WindowSec, hi - lo + 0.1);
            var start = reverse ? Math.Max(0, hi - length + 0.05) : Math.Max(0, lo - 0.05);
            _window = _mix(start, length);
            _windowStart = start;
            _windowEnd = start + _window.Length / 2.0 / SampleRate;
            if (_window.Length == 0)
                _window = new float[2];
        }
    }
}
