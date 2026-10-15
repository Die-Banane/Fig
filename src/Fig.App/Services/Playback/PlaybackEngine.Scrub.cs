using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fig.Core.Audio;
using SoundFlow.Enums;
using SoundFlow.Providers;

namespace Fig.App.Services
{
    /// <summary>
    /// Audio scrubbing (jog): while the playhead is moved by hand or by shuttle keys, plays the audio under it
    /// at the playhead's speed and direction. Separate from playback, which stays forward-only at 1x.
    /// </summary>
    public partial class PlaybackEngine
    {
        private const int ScrubBlockFrames = 480;            // 10 ms of output per step
        private const double ScrubBlockSec = ScrubBlockFrames / (double)AudioMixer.SampleRate;
        private const double ScrubMaxSpeed = 8;              // beyond this it would only be noise
        private const double ScrubJumpSec = 0.75;            // a farther move just repositions
        private const double ScrubStepBurstMinSec = 0.03;    // a single frame step plays this much, forward
        private const double ScrubStepBurstMaxSec = 0.12;
        private const double ScrubIdleStopSec = 0.5;         // device stops after this long without movement

        private readonly object _scrubLock = new();
        private QueueDataProvider? _scrubQueue;
        private QueueSource? _scrubSource;
        private ScrubSynth? _scrubSynth;
        private CancellationTokenSource? _scrubCts;
        private Task? _scrubTask;

        private double _scrubHead;                           // timeline time of the sound being produced
        private double _scrubTarget;                         // where the playhead is
        private double _scrubVelocity;                       // timeline seconds per real second, signed
        private long _scrubLastMoveTicks;
        private double _scrubBurstStart = -1;
        private double _scrubBurstLength;

        /// <summary>Whether scrubbing plays sound. Off, moving the playhead stays silent.</summary>
        public bool AudioScrubEnabled { get; set; } = true;

        /// <summary>
        /// Tells the engine the playhead was moved to <paramref name="timelineSec"/> by hand. How fast it moves
        /// sets how fast the audio plays; a single step plays a short burst at normal speed.
        /// </summary>
        public void ScrubTo(double timelineSec)
        {
            if (_disposed || _device is null || IsPlaying || !AudioScrubEnabled)
                return;

            var now = Stopwatch.GetTimestamp();
            lock (_scrubLock)
            {
                if (_scrubTask is null)
                {
                    _scrubHead = _scrubTarget = timelineSec;
                    _scrubVelocity = 0;
                    QueueBurst(timelineSec, ScrubStepBurstMaxSec);
                    _scrubTarget = timelineSec;
                    _scrubLastMoveTicks = now;
                    StartScrubSession();
                    return;
                }
                else
                {
                    var dt = (now - _scrubLastMoveTicks) / (double)Stopwatch.Frequency;
                    var delta = timelineSec - _scrubTarget;
                    if (Math.Abs(delta) < 1e-6)
                        return;

                    if (dt > 0.15 && Math.Abs(delta) <= ScrubJumpSec)
                    {
                        // a lone step (arrow key, click): hear what is at the new position
                        _scrubHead = _scrubTarget = timelineSec;
                        _scrubVelocity = 0;
                        QueueBurst(timelineSec, Math.Clamp(Math.Abs(delta), ScrubStepBurstMinSec, ScrubStepBurstMaxSec));
                    }
                    else
                    {
                        var instant = delta / Math.Max(dt, 0.004);
                        _scrubVelocity = Math.Clamp(0.5 * _scrubVelocity + 0.5 * instant, -ScrubMaxSpeed, ScrubMaxSpeed);
                        _scrubTarget = timelineSec;
                    }
                }
                _scrubTarget = timelineSec;
                _scrubLastMoveTicks = now;
            }
        }

        private void QueueBurst(double startSec, double lengthSec)
        {
            _scrubBurstStart = Math.Max(0, startSec);
            _scrubBurstLength = lengthSec;
        }

        private void StartScrubSession()
        {
            try
            {
                if (_scrubQueue is null)
                {
                    _scrubQueue = new QueueDataProvider(_format, maxSamples: AudioMixer.SampleRate,
                        fullBehavior: QueueFullBehavior.Block);
                    _scrubSource = new QueueSource(_engine, _format, _scrubQueue);
                    _scrubSource.Volume = _isMuted ? 0 : _volume;
                    _device!.MasterMixer.ConnectInput(_scrubSource);
                }
                _scrubSource!.Volume = _isMuted ? 0 : _volume;
                _scrubQueue.Reset();
                _scrubSynth = new ScrubSynth((start, length) => _mixer.Mix(_editor.Document, start, length));

                _scrubCts = new CancellationTokenSource();
                var token = _scrubCts.Token;
                _device!.Start();
                _scrubTask = Task.Factory.StartNew(() => ScrubLoop(token), token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch
            {
                // no audio device or mixer trouble: scrubbing just stays silent
                _scrubTask = null;
            }
        }

        private void ScrubLoop(CancellationToken token)
        {
            try
            {
                RunScrubLoop(token);
            }
            catch (Exception ex)
            {
                // scrub audio is a nicety: if it fails it goes quiet and must never disturb playback
                CrashLog.Write("scrub-audio", ex);
                lock (_scrubLock)
                {
                    _scrubTask = null;
                }
            }
        }

        private void RunScrubLoop(CancellationToken token)
        {
            var queue = _scrubQueue!;
            var synth = _scrubSynth!;
            var lastWasSound = false;
            long quietSince = Stopwatch.GetTimestamp();

            while (!token.IsCancellationRequested)
            {
                // stay about two blocks ahead of the device: low latency, no starvation
                if (queue.SamplesAvailable / 2 >= ScrubBlockFrames * 2)
                {
                    Thread.Sleep(2);
                    continue;
                }

                float[]? block = null;
                lock (_scrubLock)
                {
                    var now = Stopwatch.GetTimestamp();
                    var sinceMove = (now - _scrubLastMoveTicks) / (double)Stopwatch.Frequency;

                    if (_scrubBurstStart >= 0)
                    {
                        var frames = (int)Math.Round(_scrubBurstLength * AudioMixer.SampleRate);
                        block = synth.Render(_scrubBurstStart, _scrubBurstStart + _scrubBurstLength, frames,
                            fadeIn: !lastWasSound, fadeOut: true);
                        _scrubHead = _scrubBurstStart;
                        _scrubBurstStart = -1;
                    }
                    else
                    {
                        if (sinceMove > 0.12)
                            _scrubVelocity = 0;   // the hand stopped: so does the sound
                        var distance = _scrubTarget - _scrubHead;
                        if (Math.Abs(distance) > ScrubJumpSec)
                        {
                            _scrubHead = _scrubTarget;   // far behind: jump, don't chase
                            distance = 0;
                        }

                        var step = _scrubVelocity * ScrubBlockSec;
                        if (step * distance > 0)
                        {
                            if (Math.Abs(step) > Math.Abs(distance))
                                step = distance;   // never run past the playhead
                            block = synth.Render(_scrubHead, _scrubHead + step, ScrubBlockFrames,
                                fadeIn: !lastWasSound, fadeOut: false);
                            _scrubHead += step;
                        }
                    }
                }

                if (block is not null)
                {
                    queue.AddSamples(block);
                    lastWasSound = true;
                    quietSince = Stopwatch.GetTimestamp();
                    continue;
                }

                if (lastWasSound)
                {
                    // sound just stopped mid-wave: a short silent block lets the fade-out land cleanly
                    queue.AddSamples(new float[ScrubBlockFrames * 2]);
                    lastWasSound = false;
                }

                if ((Stopwatch.GetTimestamp() - quietSince) / (double)Stopwatch.Frequency > ScrubIdleStopSec)
                {
                    EndScrubFromLoop();
                    return;
                }
                Thread.Sleep(3);
            }
        }

        /// <summary>Scrubbing went quiet: release the device unless something else started it.</summary>
        private void EndScrubFromLoop()
        {
            lock (_scrubLock)
            {
                _scrubTask = null;
                _scrubCts?.Dispose();
                _scrubCts = null;
            }
            if (!IsPlaying)
                _device?.Stop();
        }

        /// <summary>Ends scrubbing before playback takes the device.</summary>
        private async Task EndScrubAsync()
        {
            Task? task;
            lock (_scrubLock)
            {
                task = _scrubTask;
                _scrubCts?.Cancel();
            }
            if (task is not null)
            {
                try { await task; }
                catch (Exception) { }   // a failed scrub must not fail playback
            }
            lock (_scrubLock)
            {
                _scrubTask = null;
                _scrubCts?.Dispose();
                _scrubCts = null;
            }
            _scrubQueue?.Reset();
            // playback starts the device itself; leave it stopped rather than started twice
            if (task is not null && !IsPlaying)
                _device?.Stop();
        }

        private void StopScrubSession()
        {
            lock (_scrubLock)
            {
                _scrubCts?.Cancel();
            }
            try { _scrubTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _scrubQueue?.Dispose();
        }
    }
}
