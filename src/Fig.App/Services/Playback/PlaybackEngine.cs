using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fig.Core.Audio;
using Fig.Core.Media;
using Fig.Core.Timeline;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Fig.App.Services
{
    public enum PlaybackState
    {
        Unavailable,
        Paused,
        Buffering,
        Playing,
        Ended,
        Faulted,
    }

    /// <summary>
    /// A <see cref="SoundComponent"/> that pulls the next chunk from the queue the
    /// producer fills. This is the bridge between the mixed ring buffer and the device.
    /// </summary>
    internal sealed class QueueSource : SoundComponent
    {
        private readonly QueueDataProvider _queue;
        public QueueSource(AudioEngine engine, AudioFormat fmt, QueueDataProvider queue) : base(engine, fmt)
        {
            _queue = queue;
            Name = "PlaybackQueue";
        }
        protected override void GenerateAudio(Span<float> buffer, int sampleRate)
        {
            var read = _queue.ReadBytes(buffer);
            if (read < buffer.Length)
                buffer.Slice(Math.Max(0, read)).Clear();   // an empty queue is silence, never stale samples
        }
    }

    /// <summary>
    /// Plays the timeline. Audio is the master clock: the device drains the queue and
    /// <see cref="PositionSec"/> derives from how many frames the device has consumed.
    /// A producer loop keeps the queue well ahead by decoding+mixing active clips through
    /// persistent audio sources (no per-chunk re-seek).
    /// </summary>
    public partial class PlaybackEngine : IDisposable
    {
        private const int ChunkFrames = 4096;             // ~85ms per decode chunk
        private const int TargetBufferedFrames = 24000;   // keep ~500ms buffered
        private const int PrebufferFrames = 12000;        // fill ~250ms before starting the device

        private readonly TimelineEditor _editor;
        private readonly AudioMixer _mixer;

        private readonly MiniAudioEngine _engine;
        private readonly AudioPlaybackDevice? _device;
        private readonly AudioFormat _format;
        private readonly QueueDataProvider _queue;
        private readonly QueueSource _source;

        private CancellationTokenSource? _cts;
        private Task? _producerTask;
        private readonly SemaphoreSlim _transportGate = new(1, 1);
        private readonly object _seekGate = new();
        private double? _pendingSeekSec;
        private bool _seekPumpRunning;

        private double _seekBaseSec;          // timeline time of queue frame 0 (after Reset, Position starts at 0)
        private bool _disposed;
        private int _positionPostPending;    // coalesce UI position posts so the dispatcher never floods
        private long _lastPositionEmitTicks; // throttle position emits so the UI isn't flooded during fill loops

        private const long MinPositionEmitTicks = 15 * TimeSpan.TicksPerMillisecond; // ~66 Hz max

        public event Action<double>? PositionChanged;
        public event Action<PlaybackState>? StateChanged;

        /// <summary>
        /// Raises <see cref="PositionChanged"/> on the UI thread. Concurrent raises are coalesced
        /// to the latest <see cref="PositionSec"/> so a slow UI never queues a backlog of
        /// stale playhead updates. Emits are also throttled to at most ~66 Hz so the audio
        /// producer fill loop doesn't flood the dispatcher.
        /// </summary>
        private void RaisePositionChanged()
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = nowTicks - _lastPositionEmitTicks;
            if (elapsedTicks > 0 && elapsedTicks < MinPositionEmitTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond)
                return;
            _lastPositionEmitTicks = nowTicks;

            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                PositionChanged?.Invoke(PositionSec);
                return;
            }

            if (System.Threading.Interlocked.CompareExchange(ref _positionPostPending, 1, 0) != 0)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                System.Threading.Interlocked.Exchange(ref _positionPostPending, 0);
                PositionChanged?.Invoke(PositionSec);
            }, Avalonia.Threading.DispatcherPriority.Render);
        }

        public PlaybackEngine(TimelineEditor editor, IMediaService media, Func<string, MediaAsset?> findAsset)
        {
            _editor = editor;
            _mixer = new AudioMixer(media, findAsset);

            _engine = new MiniAudioEngine();
            _format = new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = 2,
                SampleRate = AudioMixer.SampleRate,
            };
            _device = _engine.InitializePlaybackDevice(null, _format, new MiniAudioDeviceConfig());
            // A bounded queue with Block pacing: the producer is naturally throttled by the
            // device drain rate, so we never overflow and never starve.
            _queue = new QueueDataProvider(_format,
                maxSamples: AudioMixer.SampleRate * 4,   // ~4s headroom (interleaved => ~2s)
                fullBehavior: QueueFullBehavior.Block);
            _source = new QueueSource(_engine, _format, _queue);
            _device?.MasterMixer.ConnectInput(_source);
            State = _device is null ? PlaybackState.Unavailable : PlaybackState.Paused;
        }

        public bool IsAvailable => _device is not null;
        public bool IsPlaying { get; private set; }
        public PlaybackState State { get; private set; }
        public string? Error { get; private set; }

        private float _volume = 1;
        private bool _isMuted;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                ApplyMonitorGain();
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                ApplyMonitorGain();
            }
        }

        /// <summary>Current timeline position, driven by the audio device's consumption.</summary>
        public double PositionSec
        {
            get
            {
                if (_device is null)
                    return 0;
                // Position counts interleaved L/R floats, so /2 gives stereo frames
                return _seekBaseSec + _queue.Position / (2.0 * AudioMixer.SampleRate);
            }
        }

        public void Seek(double timelineSec)
        {
            lock (_seekGate)
            {
                _pendingSeekSec = timelineSec;
                if (_seekPumpRunning)
                    return;
                _seekPumpRunning = true;
            }
            _ = PumpSeeksAsync();
        }

        private async Task PumpSeeksAsync()
        {
            while (true)
            {
                double target;
                lock (_seekGate)
                {
                    if (_pendingSeekSec is null)
                    {
                        _seekPumpRunning = false;
                        return;
                    }
                    target = _pendingSeekSec.Value;
                    _pendingSeekSec = null;
                }
                await SeekAsync(target);
            }
        }

        public async Task SeekAsync(double timelineSec)
        {
            await _transportGate.WaitAsync();
            try
            {
                var wasPlaying = IsPlaying;
                if (wasPlaying)
                {
                    await StopProducerAsync();
                    _device?.Stop();
                    IsPlaying = false;
                }
                // the playhead may sit anywhere (past the last clip, on an empty timeline); only playback stops at the end
                _seekBaseSec = Math.Max(0, timelineSec);
                _queue.Reset();
                // while paused the decoders stay open: scrubbing mixes through them and they re-seek on their own
                if (wasPlaying)
                    await Task.Run(_mixer.ResetSources);

                if (wasPlaying && _device is not null && FramesLeft(_seekBaseSec, TimelineEnd(_editor.Document)) > 0)
                {
                    SetState(PlaybackState.Buffering);
                    await Task.Run(() => FillUntil(PrebufferFrames));
                    StartDeviceAndProducer();
                }
                else
                {
                    SetState(PlaybackState.Paused);
                }
                RaisePositionChanged();
            }
            catch (Exception ex)
            {
                Error = DescribeFailure("seek", ex);
                SetState(PlaybackState.Faulted);
            }
            finally
            {
                _transportGate.Release();
            }
        }

        public async Task PlayAsync()
        {
            await _transportGate.WaitAsync();
            try
            {
                if (_device is null)
                {
                    SetState(PlaybackState.Unavailable);
                    return;
                }
                if (IsPlaying)
                    return;

                await EndScrubAsync();
                await StopProducerAsync();
                Error = null;

                var end = TimelineEnd(_editor.Document);
                if (end <= 0)
                    return;
                if (FramesLeft(_seekBaseSec, end) == 0 || State == PlaybackState.Ended)
                    _seekBaseSec = 0;

                _queue.Reset();
                await Task.Run(_mixer.ResetSources);
                SetState(PlaybackState.Buffering);

                // Audio decoding and prebuffering can seek through a GOP and must never block UI.
                await Task.Run(() => FillUntil(PrebufferFrames));
                StartDeviceAndProducer();
            }
            catch (Exception ex)
            {
                Error = DescribeFailure("play", ex);
                IsPlaying = false;
                SetState(PlaybackState.Faulted);
            }
            finally
            {
                _transportGate.Release();
            }
        }

        /// <summary>A message that says where it went wrong; the full trace goes to the log.</summary>
        private static string DescribeFailure(string what, Exception ex) =>
            $"Playback stopped ({what}: {CrashLog.Write("playback-" + what, ex)}). Press Space to try again.";

        private void StartDeviceAndProducer()
        {
            IsPlaying = true;
            _device!.Start();
            // the token is captured here: by the time the task runs, a seek may already have cleared _cts
            var cts = new CancellationTokenSource();
            _cts = cts;
            var token = cts.Token;
            _producerTask = Task.Run(() => ProducerLoop(token));
            SetState(PlaybackState.Playing);
        }

        public async Task PauseAsync()
        {
            await _transportGate.WaitAsync();
            try
            {
                if (!IsPlaying && State != PlaybackState.Buffering)
                    return;
                var position = PositionSec;
                IsPlaying = false;
                await StopProducerAsync();
                _device?.Stop();
                _seekBaseSec = Math.Max(0, position);
                _queue.Reset();
                await Task.Run(_mixer.ResetSources);
                SetState(PlaybackState.Paused);
                RaisePositionChanged();
            }
            finally
            {
                _transportGate.Release();
            }
        }

        /// <summary>Decode+mix ahead of the device until cancelled or the timeline ends.</summary>
        private async Task ProducerLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var bufferedFrames = _queue.SamplesAvailable / 2;
                    if (bufferedFrames < TargetBufferedFrames)
                    {
                        if (!EnqueueOneChunk())
                        {
                            if (_queue.SamplesAvailable == 0)
                            {
                                CompletePlaybackFromProducer();
                                return;
                            }
                            await Task.Delay(10, token);
                            RaisePositionChanged();
                            continue;
                        }
                        if (_queue.SamplesAvailable / 2 < TargetBufferedFrames)
                        {
                            RaisePositionChanged();
                            continue;
                        }
                    }

                    RaisePositionChanged();
                    await Task.Delay(10, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        /// <summary>Mixes chunks until at least <paramref name="frameCount"/> frames are buffered (or timeline ends).</summary>
        private void FillUntil(int frameCount)
        {
            while (_queue.SamplesAvailable / 2 < frameCount)
            {
                if (!EnqueueOneChunk())
                    break;
            }
        }

        /// <summary>Enqueues one mix chunk. Returns false when the timeline has ended.</summary>
        private bool EnqueueOneChunk()
        {
            var timeline = _editor.Document;
            var timelineEnd = TimelineEnd(timeline);
            var produced = _queue.TotalSamplesEnqueued / 2;
            var startSec = _seekBaseSec + produced / (double)AudioMixer.SampleRate;
            // less than half a frame left mixes to an empty chunk, which would never advance the playhead
            var frames = Math.Min(ChunkFrames, FramesLeft(startSec, timelineEnd));
            if (frames == 0)
                return false;

            var chunk = _mixer.Mix(timeline, startSec, frames / (double)AudioMixer.SampleRate);
            _queue.AddSamples(chunk);
            return true;
        }

        /// <summary>Whole frames between <paramref name="startSec"/> and <paramref name="endSec"/>, rounded the way the mixer rounds.</summary>
        internal static int FramesLeft(double startSec, double endSec) =>
            endSec <= startSec ? 0 : (int)Math.Min(int.MaxValue, Math.Round((endSec - startSec) * AudioMixer.SampleRate));

        private async Task StopProducerAsync()
        {
            var cts = _cts;
            var producer = _producerTask;
            _cts = null;
            _producerTask = null;
            if (cts is null)
                return;
            cts.Cancel();
            _queue.Reset();
            if (producer is not null && producer.Id != Task.CurrentId)
            {
                try { await producer; }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // the producer is being torn down anyway; a seek must not fail because of how it ended
                    CrashLog.Write("playback-producer", ex);
                }
            }
            cts.Dispose();
        }

        private void CompletePlaybackFromProducer()
        {
            _device?.Stop();
            _seekBaseSec = TimelineEnd(_editor.Document);
            _queue.Reset();
            IsPlaying = false;
            SetState(PlaybackState.Ended);
            RaisePositionChanged();
        }

        private void ApplyMonitorGain()
        {
            if (!_disposed)
                _source.Volume = _isMuted ? 0 : _volume;
        }

        private void SetState(PlaybackState state)
        {
            if (State == state)
                return;
            State = state;
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                StateChanged?.Invoke(state);
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(state));
        }

        private static double TimelineEnd(Timeline timeline)
        {
            double end = 0;
            foreach (var track in timeline.Tracks)
                foreach (var clip in track.Clips)
                {
                    if (!clip.Enabled)
                        continue;
                    end = Math.Max(end, clip.StartSec + clip.DurSec);
                }
            return end;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            IsPlaying = false;
            StopScrubSession();
            _cts?.Cancel();
            _queue.Reset();
            try { _producerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts?.Dispose();
            _transportGate.Dispose();
            _mixer.Dispose();
            _queue.Dispose();
            _device?.Dispose();
            _engine.Dispose();
        }
    }
}
