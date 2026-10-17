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

        /// <summary>
        /// Half a source frame. A frame is shown for a time when it is the nearest one: a frame up to this much
        /// before the time counts. Containers round frame times (WebM to the millisecond, so 29.97 fps frames sit
        /// at 0.033, 0.067...), and asking for exactly 0.0334 must still get the 0.033 frame, not skip to the
        /// next one and then show that one twice.
        /// </summary>
        private readonly double _halfFrameSec;

        private AVFrame* _pendingFrame;
        // a packet the decoder refused (EAGAIN: it still holds decoded frames); sent again before reading on
        private AVPacket* _heldPacket;

        // smoothing (DecodeSmoothed). Blending keeps the frame shown before the current one: the two output
        // buffers swap, so it costs no copy. Motion blur adds up every frame a step passes, from _accumFrom on.
        private bool _keepPrevious;
        private byte[] _prevPixels = Array.Empty<byte>();
        private double _prevSec = -1;
        private double _canvasSec = -1;
        private byte[] _mixScratch = Array.Empty<byte>();
        private int[] _accum = Array.Empty<int>();
        private double _accumFrom = double.NaN;
        private int _accumCount;
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
                inCtx = FfmpegOpen.Input(sourcePath);

                var vIdx = FfmpegOpen.BestStream(inCtx, AVMediaType.AVMEDIA_TYPE_VIDEO);
                var stream = inCtx->streams[vIdx];
                var par = stream->codecpar;

                decCtx = FfmpegOpen.Decoder(stream, threads: Math.Clamp(Environment.ProcessorCount / 2, 2, 8));

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
                var reported = stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0 ? stream->avg_frame_rate : stream->r_frame_rate;
                var plausible = reported.num > 0 && reported.den > 0
                    ? Fig.Core.Timeline.FrameRate.Plausible(new Fig.Core.Timeline.FrameRate(reported.num, reported.den))
                    : null;
                // a junk rate (a recording's 1000/1): assume frames no closer than 60 fps
                _halfFrameSec = 0.5 / (plausible?.Fps ?? 60);
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
                _heldPacket = ffmpeg.av_packet_alloc();
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
            ffmpeg.av_packet_unref(_heldPacket);
            _prevSec = -1;
            _canvasSec = -1;
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
                // still nearest the frame on screen
                if (_lastPresentedSec >= 0 && timeSec < _lastPresentedSec + _halfFrameSec)
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
            if (_lastPresentedSec >= 0 && timeSec >= _lastPresentedSec + _halfFrameSec && timeSec - _lastPresentedSec <= ScrubContinueSec)
                return DecodeSequential(packet, timeSec, catchUp: false);

            if (!_seekedSinceFrame)
                Seek(timeSec);

            var ctx = _inCtx;
            var dec = _decCtx;
            int ret;
            while ((ret = ReadPacket(packet)) >= 0)
            {
                if (packet->stream_index == _vIdx && SendPacket(packet)
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
                while ((ret = ReadPacket(packet)) >= 0)
                {
                    if (Aborted())
                    {
                        // a newer position arrived: stop here, keeping the packet for the decode that carries on
                        ffmpeg.av_packet_move_ref(_heldPacket, packet);
                        _lastPresentedSec = -1;
                        _lastApproximate = true;
                        return _lastFrame;
                    }
                    if (packet->stream_index == _vIdx && SendPacket(packet))
                    {
                        while (ffmpeg.avcodec_receive_frame(dec, _pendingFrame) == 0)
                        {
                            var sec = _pendingFrame->best_effort_timestamp * ffmpeg.av_q2d(_timeBase);
                            var nearest = sec > targetSec - _halfFrameSec;
                            if (!nearest && !double.IsNaN(_accumFrom) && sec >= _accumFrom)
                                Accumulate(ScalePendingToLastFrame(sec));   // motion blur: this frame counts too
                            if (!nearest)
                            {
                                lastSeenSec = sec;
                                ffmpeg.av_frame_unref(_tailFrame);
                                ffmpeg.av_frame_ref(_tailFrame, _pendingFrame);
                            }
                            else
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
                    if (sec > targetSec - _halfFrameSec)
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

        private DecodedFrame ScalePendingToLastFrame() => ScalePendingToLastFrame(_lastPresentedSec);

        /// <param name="sec">The time of the frame being scaled.</param>
        private DecodedFrame ScalePendingToLastFrame(double sec)
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
            // blending: the frame on the canvas becomes the previous one, and the next is drawn into the other buffer
            if (_keepPrevious && _canvasSec >= 0 && sec > _canvasSec)
            {
                if (_prevPixels.Length < byteCount)
                    _prevPixels = new byte[byteCount];
                (_prevPixels, _outputScratch) = (_outputScratch, _prevPixels);
                _prevSec = _canvasSec;
            }
            _canvasSec = sec;
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

        /// <summary>
        /// A frame for <paramref name="timeSec"/> smoothed for its speed. <paramref name="spanSec"/> is how much of the
        /// file one output frame covers there (the clip's speed over one project frame). Slower than the file's own
        /// frames and <paramref name="blend"/>: the two real frames around the moment, mixed by how far between them
        /// it is, so slow motion glides. Faster, with <paramref name="motionBlur"/> (0..1, the shutter): the frames the
        /// step passes over, averaged, so fast motion smears. Scrubbing always takes the plain frame.
        /// </summary>
        public DecodedFrame? DecodeSmoothed(double timeSec, double spanSec, bool blend, double motionBlur, PreviewDecodeMode mode)
        {
            var frameSec = 2 * _halfFrameSec;
            if (mode == PreviewDecodeMode.Scrub)
            {
                _keepPrevious = false;
                return DecodeForward(timeSec, mode);
            }
            if (motionBlur > 0 && spanSec > frameSec * 1.25)
            {
                _keepPrevious = false;
                return DecodeBlurred(timeSec, spanSec * Math.Clamp(motionBlur, 0, 1), mode);
            }
            if (blend && spanSec < frameSec * 0.95)
                return DecodeBlended(timeSec, mode);
            _keepPrevious = false;
            return DecodeForward(timeSec, mode);
        }

        private DecodedFrame? DecodeBlended(double timeSec, PreviewDecodeMode mode)
        {
            _keepPrevious = true;
            // the first real frame after the moment; the one shown before it is the frame at or before it
            var after = DecodeForward(timeSec + _halfFrameSec + 1e-6, mode);
            var afterSec = _lastPresentedSec;
            if (after is null || _prevSec < 0 || _prevSec > timeSec + 1e-6 || afterSec - _prevSec > 2 * _halfFrameSec * 1.6)
                return after;   // no neighbour to blend with (just sought, or a frame was skipped)
            var mix = Math.Clamp((timeSec - _prevSec) / Math.Max(1e-9, afterSec - _prevSec), 0, 1);
            var count = _outW * _outH * 4;
            if (_mixScratch.Length < count)
                _mixScratch = new byte[count];
            var w = (int)Math.Round(mix * 256);
            fixed (byte* a = _prevPixels, b = after.Pixels, o = _mixScratch)
                for (var i = 0; i < count; i++)
                    o[i] = (byte)((a[i] * (256 - w) + b[i] * w) >> 8);
            return new DecodedFrame { Width = _outW, Height = _outH, Pixels = _mixScratch };
        }

        private DecodedFrame? DecodeBlurred(double timeSec, double windowSec, PreviewDecodeMode mode)
        {
            var count = _outW * _outH * 4;
            if (_accum.Length < count)
                _accum = new int[count];
            Array.Clear(_accum, 0, count);
            _accumCount = 0;
            _accumFrom = timeSec - windowSec;
            DecodedFrame? frame;
            try
            {
                var before = _lastPresentedSec;
                frame = DecodeForward(timeSec, mode);
                if (frame is null || _lastPresentedSec == before)
                    return frame;   // the frame on screen still stands: nothing new to smear
                Accumulate(frame);
            }
            finally
            {
                _accumFrom = double.NaN;
            }
            if (_accumCount <= 1)
                return frame;
            if (_mixScratch.Length < count)
                _mixScratch = new byte[count];
            var n = _accumCount;
            fixed (int* acc = _accum)
            fixed (byte* o = _mixScratch)
                for (var i = 0; i < count; i++)
                    o[i] = (byte)(acc[i] / n);
            return new DecodedFrame { Width = _outW, Height = _outH, Pixels = _mixScratch };
        }

        private void Accumulate(DecodedFrame frame)
        {
            var count = Math.Min(_outW * _outH * 4, frame.Pixels.Length);
            fixed (int* acc = _accum)
            fixed (byte* p = frame.Pixels)
                for (var i = 0; i < count; i++)
                    acc[i] += p[i];
            _accumCount++;
        }

        /// <summary>The packet the decoder refused last time, if any, else the next one from the file.</summary>
        private int ReadPacket(AVPacket* packet)
        {
            if (_heldPacket->size > 0 || _heldPacket->data != null)
            {
                ffmpeg.av_packet_move_ref(packet, _heldPacket);
                return 0;
            }
            return ffmpeg.av_read_frame(_inCtx, packet);
        }

        /// <summary>
        /// Sends a packet to the decoder. True when there may be frames to take: it was accepted, or the decoder is
        /// full (EAGAIN) and must give up its frames first; then the packet is held and sent again next.
        /// Dropping it instead would starve every frame that refers to it, and a threaded VP9 decoder is full often.
        /// </summary>
        private bool SendPacket(AVPacket* packet)
        {
            var ret = ffmpeg.avcodec_send_packet(_decCtx, packet);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                ffmpeg.av_packet_move_ref(_heldPacket, packet);
                return true;
            }
            return ret >= 0;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_heldPacket != null)
            {
                var hp = _heldPacket;
                ffmpeg.av_packet_free(&hp);
                _heldPacket = null;
            }
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
