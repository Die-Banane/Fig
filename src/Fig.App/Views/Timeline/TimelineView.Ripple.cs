using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Fig.App.Services;
using Fig.App.ViewModels;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>The short slide animation clips play when a ripple edit moves them.</summary>
    public partial class TimelineView
    {
        private DispatcherTimer? _rippleTimer;

        private const double RippleAnimDurationMs = 220;

        private void OnRippleSlideStarted(IReadOnlyList<RippleSlideDelta> deltas)
        {
            var now = Stopwatch.GetTimestamp();
            foreach (var d in deltas)
                _rippleSlides[d.ClipId] = (d.FromOffsetSec, now);
            EnsureRippleTimer();
            InvalidateVisual();
        }

        private void EnsureRippleTimer()
        {
            if (_rippleTimer is not null)
                return;
            _rippleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _rippleTimer.Tick += OnRippleTimerTick;
            _rippleTimer.Start();
        }

        private void OnRippleTimerTick(object? sender, EventArgs e)
        {
            if (_rippleSlides.Count == 0)
            {
                StopRippleTimer();
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var freq = (double)Stopwatch.Frequency;
            List<string>? done = null;

            foreach (var (id, state) in _rippleSlides)
            {
                var elapsedMs = (now - state.StartTimestamp) * 1000.0 / freq;
                if (elapsedMs >= RippleAnimDurationMs)
                    (done ??= new List<string>()).Add(id);
            }

            if (done is not null)
            {
                foreach (var id in done)
                    _rippleSlides.Remove(id);
            }

            InvalidateVisual();

            if (_rippleSlides.Count == 0)
                StopRippleTimer();
        }

        private void StopRippleTimer()
        {
            if (_rippleTimer is null)
                return;
            _rippleTimer.Tick -= OnRippleTimerTick;
            _rippleTimer.Stop();
            _rippleTimer = null;
        }

        private void ClearRippleSlides()
        {
            _rippleSlides.Clear();
            StopRippleTimer();
        }

        /// <summary>
        /// Current draw offset in seconds for a clip (eases from FromOffset → 0).
        /// Positive offset draws the clip to the right of its committed StartSec.
        /// </summary>
        private double GetRippleOffsetSec(string clipId)
        {
            if (!_rippleSlides.TryGetValue(clipId, out var state))
                return 0;

            var elapsedMs = (Stopwatch.GetTimestamp() - state.StartTimestamp) * 1000.0 / Stopwatch.Frequency;
            var t = Math.Clamp(elapsedMs / RippleAnimDurationMs, 0, 1);
            // ease-out cubic
            var eased = 1 - Math.Pow(1 - t, 3);
            return state.FromOffsetSec * (1 - eased);
        }
    }
}
