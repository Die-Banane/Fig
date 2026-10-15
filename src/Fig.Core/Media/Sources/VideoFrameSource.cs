using System;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace Fig.Core.Media
{
    /// <summary>Decode quality vs speed tradeoff for preview.</summary>
    public enum PreviewDecodeMode
    {
        /// <summary>Forward play: decode every frame, accurate timing.</summary>
        Playback,
        /// <summary>Scrub/seek: show the nearest keyframe at once (see LastFrameApproximate).</summary>
        Scrub,
    }

    /// <summary>
    /// Persistent sequential video decoder tuned for preview:
    /// multi-threaded decode, reused sws/buffers, optional scrub skip-frame mode.
    /// </summary>
    public sealed unsafe class VideoFrameSource : IVideoFrameSource
    {
        private const int SwsBilinear = 2;
        private const double FastCatchUpSec = 0.5;

        /// <summary>A scrub step this close ahead of the last frame just keeps decoding (exact, a few frames).</summary>
        private const double ScrubContinueSec = 0.35;

        /// <summary>
        /// Farther ahead than this, re-seeking to the keyframe before the target beats decoding every frame
        /// in between.
        /// </summary>
        private const double SeekInsteadSec = 1.5;

        private readonly AVFormatContext* _inCtx;
        private readonly AVCodecContext* _decCtx;
        private readonly int _vIdx;
        private readonly int _srcW, _srcH;
        private readonly int _outW, _outH;
        private readonly int _fitW, _fitH;
        private readonly int _offX, _offY;
        private readonly AVRational _timeBase;

        private AVFrame* _pendingFrame;
        private AVFrame* _tailFrame;   // the newest frame seen, kept so the picture can hold at the end of the media
        private AVFrame* _rgbFrame;
        private SwsContext* _sws;
        private AVPixelFormat _swsSrcFmt = AVPixelFormat.AV_PIX_FMT_NONE;
        private double _lastPresentedSec = -1;
        private DecodedFrame? _lastFrame;
        private PreviewDecodeMode _mode = PreviewDecodeMode.Playback;
        private bool _disposed;
        private readonly double _lastFrameSec = double.PositiveInfinity;   // start of the final frame, when known
        private bool _lastApproximate;
        private bool _seekedSinceFrame;       // sought and nothing decoded since
        private bool _atKeyframeAfterSeek;    // only the keyframe after a seek has been decoded

        // Reused output buffer: frames are consumed synchronously or copied (e.g. into the
        // frame cache) before the next decode, so one scratch per source avoids a per-frame
        // full-canvas allocation.
        private byte[] _outputScratch = Array.Empty<byte>();

        public double LastPresentedTimeSec => _lastPresentedSec;

        public bool LastFrameApproximate => _lastApproximate;

        public Func<bool>? AbortRequested { get; set; }

        private bool Aborted() => AbortRequested?.Invoke() == true;

        public PreviewDecodeMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                    return;
                _mode = value;
                ApplySkipPolicy();
            }
        }

        /// <summary>
        /// Opens a sequential decoder that produces full-canvas frames of
        /// <paramref name="width"/>×<paramref name="height"/>. The source is scaled to the
        /// largest centered region that fits the canvas while preserving its aspect ratio —
        /// the rest of the canvas stays black. This keeps the compositor simple (every layer
        /// is full-canvas) while never stretching the picture.
        /// </summary>
        internal VideoFrameSource(string sourcePath, int width, int height)
        {
            AVFormatContext* inCtx = null;
            AVCodecContext* decCtx = null;

            try
            {
                var pIn = inCtx;
                ThrowIfError(ffmpeg.avformat_open_input(&pIn, sourcePath, null, null), "avformat_open_input");
                inCtx = pIn;
                ThrowIfError(ffmpeg.avformat_find_stream_info(inCtx, null), "avformat_find_stream_info");

                var vIdx = ffmpeg.av_find_best_stream(inCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
                ThrowIfError(vIdx, "av_find_best_stream(video)");
                var stream = inCtx->streams[vIdx];
                var par = stream->codecpar;

                var dec = ffmpeg.avcodec_find_decoder(par->codec_id);
                if (dec == null)
                    throw new InvalidOperationException("No video decoder found");
                decCtx = ffmpeg.avcodec_alloc_context3(dec);
                ThrowIfError(ffmpeg.avcodec_parameters_to_context(decCtx, par), "avcodec_parameters_to_context");
                decCtx->pkt_timebase = stream->time_base;

                // Multi-threaded decode for smoother preview/scrub. Safe now that decoders are
                // only ever disposed on the single decode worker thread, never concurrently with
                // an in-flight decode (the previous single-threaded workaround for a teardown
                // crash is obsolete).
                decCtx->thread_count = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

                ThrowIfError(ffmpeg.avcodec_open2(decCtx, dec, null), "avcodec_open2");

                _inCtx = inCtx;
                _decCtx = decCtx;
                _vIdx = vIdx;
                _srcW = par->width;
                _srcH = par->height;
                _outW = width;
                _outH = height;
                _timeBase = stream->time_base;

                // where the final frame starts: asking for later holds that frame instead of seeking off the end
                var fpsRational = stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0
                    ? ffmpeg.av_q2d(stream->avg_frame_rate) : 30.0;
                if (inCtx->duration > 0)
                {
                    var startSec = inCtx->start_time != ffmpeg.AV_NOPTS_VALUE ? inCtx->start_time / 1_000_000.0 : 0;
                    _lastFrameSec = Math.Max(0, startSec + inCtx->duration / 1_000_000.0 - 1.5 / Math.Max(1, fpsRational));
                }

                // largest centered region that fits the canvas while preserving aspect
                var scale = Math.Min(width / (double)Math.Max(1, _srcW), height / (double)Math.Max(1, _srcH));
                _fitW = Math.Max(1, (int)Math.Round(_srcW * scale));
                _fitH = Math.Max(1, (int)Math.Round(_srcH * scale));
                _offX = (width - _fitW) / 2;
                _offY = (height - _fitH) / 2;

                _pendingFrame = ffmpeg.av_frame_alloc();
                _tailFrame = ffmpeg.av_frame_alloc();
                EnsureRgbFrame();
                ApplySkipPolicy();
            }
            catch
            {
                if (decCtx != null) ffmpeg.avcodec_free_context(&decCtx);
                if (inCtx != null)
                {
                    var p = inCtx;
                    ffmpeg.avformat_close_input(&p);
                }
                throw;
            }
        }

        private void ApplySkipPolicy()
        {
            if (_decCtx == null)
                return;
            // Scrub: drop non-reference frames so seek-forward through GOPs is cheaper.
            // Playback: decode everything for smooth forward motion.
            _decCtx->skip_frame = _mode == PreviewDecodeMode.Scrub
                ? AVDiscard.AVDISCARD_NONREF
                : AVDiscard.AVDISCARD_DEFAULT;
            _decCtx->skip_idct = _mode == PreviewDecodeMode.Scrub
                ? AVDiscard.AVDISCARD_NONREF
                : AVDiscard.AVDISCARD_DEFAULT;
            _decCtx->skip_loop_filter = _mode == PreviewDecodeMode.Scrub
                ? AVDiscard.AVDISCARD_NONREF
                : AVDiscard.AVDISCARD_DEFAULT;
        }

        private void EnsureRgbFrame()
        {
            if (_rgbFrame != null)
                return;
            _rgbFrame = ffmpeg.av_frame_alloc();
            _rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            _rgbFrame->width = _fitW;
            _rgbFrame->height = _fitH;
            ThrowIfError(ffmpeg.av_frame_get_buffer(_rgbFrame, 32), "av_frame_get_buffer");
        }

        private void EnsureSws(AVPixelFormat srcFmt)
        {
            if (_sws != null && _swsSrcFmt == srcFmt)
                return;
            if (_sws != null)
            {
                ffmpeg.sws_freeContext(_sws);
                _sws = null;
            }
            _sws = ffmpeg.sws_getContext(
                _srcW, _srcH, srcFmt,
                _fitW, _fitH, AVPixelFormat.AV_PIX_FMT_BGRA,
                SwsBilinear, null, null, null);
            _swsSrcFmt = srcFmt;
        }

        public void Seek(double timeSec)
        {
            var ctx = _inCtx;
            var dec = _decCtx;
            var targetTs = (long)(Math.Max(0, timeSec) * _timeBase.den / _timeBase.num);
            var ret = ffmpeg.av_seek_frame(ctx, _vIdx, Math.Max(0, targetTs), ffmpeg.AVSEEK_FLAG_BACKWARD);
            if (ret < 0)
                ffmpeg.av_seek_frame(ctx, _vIdx, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
            ffmpeg.avcodec_flush_buffers(dec);
            _lastPresentedSec = -1;
            _seekedSinceFrame = true;
            _atKeyframeAfterSeek = false;
            // Keep _lastFrame so scrub can show something until the new decode lands.
        }

        public DecodedFrame? DecodeForward(double timeSec)
            => DecodeForward(timeSec, _mode);

        public DecodedFrame? DecodeForward(double timeSec, PreviewDecodeMode mode)
        {
            Mode = mode;

            var ctx = _inCtx;
            var dec = _decCtx;
            AVPacket* packet = ffmpeg.av_packet_alloc();
            try
            {
                timeSec = Math.Min(timeSec, _lastFrameSec);
                if (_lastPresentedSec >= 0 && timeSec <= _lastPresentedSec + 1e-4)
                    return _lastFrame;

                _lastApproximate = false;
                if (mode == PreviewDecodeMode.Scrub)
                    return DecodeScrub(packet, timeSec);

                // far ahead (or after a gap): the keyframe just before the target is nearer than the last frame
                if (_lastPresentedSec >= 0 && !_atKeyframeAfterSeek && timeSec - _lastPresentedSec > SeekInsteadSec)
                    Seek(timeSec);

                // After a seek (_lastPresentedSec < 0) or when the decode has fallen far behind the
                // target, fast-forward by dropping B-frames (never referenced) to reach the target's
                // neighborhood, then decode precisely for the tail.
                var catchUp = _lastPresentedSec < 0 || timeSec - _lastPresentedSec > FastCatchUpSec;
                return DecodeSequential(packet, timeSec, catchUp);
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
            }
        }

        /// <summary>
        /// Interactive scrub: a small step forward keeps decoding (exact); anything else shows the nearest
        /// keyframe at or before the target right away. The caller settles on the exact frame afterwards
        /// with a Playback decode, which continues from here instead of seeking again.
        /// </summary>
        private DecodedFrame? DecodeScrub(AVPacket* packet, double timeSec)
        {
            if (_lastPresentedSec >= 0 && timeSec > _lastPresentedSec && timeSec - _lastPresentedSec <= ScrubContinueSec)
                return DecodeSequential(packet, timeSec, catchUp: false);

            if (!_seekedSinceFrame)
                Seek(timeSec);

            var ctx = _inCtx;
            var dec = _decCtx;
            int ret;
            while ((ret = ffmpeg.av_read_frame(ctx, packet)) >= 0)
            {
                if (packet->stream_index == _vIdx && ffmpeg.avcodec_send_packet(dec, packet) >= 0
                    && ffmpeg.avcodec_receive_frame(dec, _pendingFrame) == 0)
                {
                    ffmpeg.av_packet_unref(packet);
                    _lastPresentedSec = _pendingFrame->best_effort_timestamp * ffmpeg.av_q2d(_timeBase);
                    _seekedSinceFrame = false;
                    _atKeyframeAfterSeek = true;
                    _lastApproximate = timeSec - _lastPresentedSec > 0.02;
                    return ScalePendingToLastFrame();
                }
                ffmpeg.av_packet_unref(packet);
            }
            return _lastFrame;
        }

        private DecodedFrame? DecodeSequential(AVPacket* packet, double targetSec, bool catchUp)
        {
            var ctx = _inCtx;
            var dec = _decCtx;

            // While far behind the target, drop B-frames (never referenced) so the reference
            // stream (I/P frames) advances quickly toward it; switch back to full decode for
            // the precise tail. skip_frame is restored in finally so the mode policy always
            // wins after this call.
            _decCtx->skip_frame = catchUp ? AVDiscard.AVDISCARD_NONREF : AVDiscard.AVDISCARD_DEFAULT;
            var lastSeenSec = -1.0;
            ffmpeg.av_frame_unref(_tailFrame);
            try
            {
                int ret;
                while ((ret = ffmpeg.av_read_frame(ctx, packet)) >= 0)
                {
                    if (Aborted())
                    {
                        // a newer position arrived: stop here and force a fresh seek next time
                        ffmpeg.av_packet_unref(packet);
                        _lastPresentedSec = -1;
                        _lastApproximate = true;
                        return _lastFrame;
                    }
                    if (packet->stream_index == _vIdx && ffmpeg.avcodec_send_packet(dec, packet) >= 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(dec, _pendingFrame) == 0)
                        {
                            var sec = _pendingFrame->best_effort_timestamp * ffmpeg.av_q2d(_timeBase);
                            if (sec < targetSec)
                            {
                                lastSeenSec = sec;
                                ffmpeg.av_frame_unref(_tailFrame);
                                ffmpeg.av_frame_ref(_tailFrame, _pendingFrame);
                            }
                            if (sec >= targetSec)
                            {
                                _lastPresentedSec = sec;
                                _seekedSinceFrame = false;
                                _atKeyframeAfterSeek = false;
                                ffmpeg.av_packet_unref(packet);
                                return ScalePendingToLastFrame();
                            }
                            // inside the tail window: resume full decode so the presented
                            // frame is exact (the fast-forward only skips the approach)
                            if (catchUp && sec >= targetSec - FastCatchUpSec)
                            {
                                catchUp = false;
                                _decCtx->skip_frame = AVDiscard.AVDISCARD_DEFAULT;
                            }
                        }
                    }
                    ffmpeg.av_packet_unref(packet);
                }

                ffmpeg.avcodec_send_packet(dec, null);
                while (ffmpeg.avcodec_receive_frame(dec, _pendingFrame) == 0)
                {
                    var sec = _pendingFrame->best_effort_timestamp * ffmpeg.av_q2d(_timeBase);
                    if (sec >= targetSec)
                    {
                        _lastPresentedSec = sec;
                        return ScalePendingToLastFrame();
                    }
                    lastSeenSec = sec;
                    ffmpeg.av_frame_unref(_tailFrame);
                    ffmpeg.av_frame_ref(_tailFrame, _pendingFrame);
                }

                // the media ends before the target: hold its final frame (a source that never showed one stays empty)
                if (lastSeenSec >= 0 && _lastFrame != null && _tailFrame->data[0] != null)
                {
                    ffmpeg.av_frame_unref(_pendingFrame);
                    ffmpeg.av_frame_move_ref(_pendingFrame, _tailFrame);
                    _lastPresentedSec = lastSeenSec;
                    _seekedSinceFrame = false;
                    _atKeyframeAfterSeek = false;
                    return ScalePendingToLastFrame();
                }
                return _lastFrame;
            }
            finally
            {
                _decCtx->skip_frame = _mode == PreviewDecodeMode.Scrub
                    ? AVDiscard.AVDISCARD_NONREF
                    : AVDiscard.AVDISCARD_DEFAULT;
            }
        }

        private DecodedFrame ScalePendingToLastFrame()
        {
            EnsureRgbFrame();
            var srcFmt = (AVPixelFormat)_pendingFrame->format;
            // Some codecs report the format on the context until the first frame.
            if (srcFmt == AVPixelFormat.AV_PIX_FMT_NONE)
                srcFmt = _decCtx->pix_fmt;
            EnsureSws(srcFmt);
            if (_sws == null)
                return _lastFrame!;

            // Even dimensions for YUV420 sources — scale from actual frame size when present.
            var srcH = _pendingFrame->height > 0 ? _pendingFrame->height : _srcH;
            ffmpeg.sws_scale(_sws, _pendingFrame->data, _pendingFrame->linesize, 0, srcH,
                _rgbFrame->data, _rgbFrame->linesize);

            var byteCount = _outW * _outH * 4;
            if (_outputScratch.Length < byteCount)
            {
                _outputScratch = new byte[byteCount];
                Array.Clear(_outputScratch, 0, _outputScratch.Length);   // letterbox bars = black
            }
            var canvas = _outputScratch;

            // Paste the aspect-correct fit region into the center of the canvas. Both the
            // fit frame and the canvas use bottom-up row order, so fit row y lands on
            // canvas row (y + _offY).
            var fitRowBytes = _fitW * 4;
            var canvasRowBytes = _outW * 4;
            fixed (byte* dstBase = canvas)
            {
                for (var y = 0; y < _fitH; y++)
                {
                    var src = _rgbFrame->data[0] + y * _rgbFrame->linesize[0];
                    var dst = dstBase + (y + _offY) * canvasRowBytes + _offX * 4;
                    Buffer.MemoryCopy(src, dst, fitRowBytes, fitRowBytes);
                }
            }

            _lastFrame = new DecodedFrame { Width = _outW, Height = _outH, Pixels = canvas };
            return _lastFrame;
        }

        private static void ThrowIfError(int ret, string what)
        {
            if (ret < 0)
                throw new InvalidOperationException($"{what} failed: {ret}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_sws != null)
            {
                ffmpeg.sws_freeContext(_sws);
                _sws = null;
            }
            if (_rgbFrame != null)
            {
                var f = _rgbFrame;
                ffmpeg.av_frame_free(&f);
                _rgbFrame = null;
            }
            if (_pendingFrame != null)
            {
                var f = _pendingFrame;
                ffmpeg.av_frame_free(&f);
                _pendingFrame = null;
            }
            if (_tailFrame != null)
            {
                var f = _tailFrame;
                ffmpeg.av_frame_free(&f);
                _tailFrame = null;
            }
            if (_decCtx != null)
            {
                var d = _decCtx;
                ffmpeg.avcodec_free_context(&d);
            }
            if (_inCtx != null)
            {
                var p = _inCtx;
                ffmpeg.avformat_close_input(&p);
            }
        }
    }
}
