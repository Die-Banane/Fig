using System;
using System.Collections.Generic;
using System.Globalization;
using FFmpeg.AutoGen;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// How the finished video lies in the file: as edited, or turned a quarter so a wide edit fills a phone held
    /// sideways (the file is tall, the picture on its side).
    /// </summary>
    public enum ExportTurn
    {
        None,
        /// <summary>Turned clockwise: watched with the phone turned to the left.</summary>
        Right,
        /// <summary>Turned anticlockwise: watched with the phone turned to the right.</summary>
        Left,
    }

    /// <summary>Which H.264 encoder an export uses.</summary>
    public enum VideoEncoderChoice
    {
        /// <summary>The first hardware encoder that opens on this machine, else the CPU.</summary>
        Automatic,
        /// <summary>NVIDIA NVENC.</summary>
        Nvenc,
        /// <summary>Intel Quick Sync.</summary>
        QuickSync,
        /// <summary>AMD AMF (Windows).</summary>
        Amf,
        /// <summary>VA-API: AMD and Intel GPUs on Linux.</summary>
        Vaapi,
        /// <summary>libx264 on the CPU: slowest, smallest files at a given quality.</summary>
        Software,
    }

    /// <summary>An encoder that opened, ready for frames in <see cref="InputFormat"/>.</summary>
    public sealed unsafe class OpenedEncoder : IDisposable
    {
        public AVCodecContext* Context { get; internal set; }
        public AVCodec* Codec { get; internal set; }
        /// <summary>FFmpeg's name for it ("h264_nvenc").</summary>
        public string Name { get; internal set; } = "";
        /// <summary>What to call it ("NVIDIA NVENC").</summary>
        public string Label { get; internal set; } = "";
        /// <summary>The picture format frames are converted to before sending (or uploading).</summary>
        public AVPixelFormat InputFormat { get; internal set; }
        /// <summary>For an encoder that takes GPU frames (VA-API): the pool each frame is uploaded into. Else null.</summary>
        public AVBufferRef* HwFrames { get; internal set; }
        internal AVBufferRef* HwDevice;

        public void Dispose()
        {
            var ctx = Context;
            if (ctx != null)
                ffmpeg.avcodec_free_context(&ctx);
            Context = null;
            var frames = HwFrames;
            if (frames != null)
                ffmpeg.av_buffer_unref(&frames);
            HwFrames = null;
            var device = HwDevice;
            if (device != null)
                ffmpeg.av_buffer_unref(&device);
            HwDevice = null;
        }
    }

    /// <summary>
    /// Opens an H.264 encoder for an export: a GPU one when there is one, else libx264. The dialog's quality (CRF,
    /// 0 best to 51 worst) maps to each encoder's own constant-quality setting.
    /// </summary>
    public static unsafe class VideoEncoders
    {
        private sealed record Entry(VideoEncoderChoice Choice, string Name, string Label);

        private static readonly Entry[] Hardware =
        [
            new(VideoEncoderChoice.Nvenc, "h264_nvenc", "NVIDIA NVENC"),
            new(VideoEncoderChoice.QuickSync, "h264_qsv", "Intel Quick Sync"),
            new(VideoEncoderChoice.Amf, "h264_amf", "AMD AMF"),
            new(VideoEncoderChoice.Vaapi, "h264_vaapi", "VA-API"),
        ];

        private static readonly Entry Software = new(VideoEncoderChoice.Software, "libx264", "CPU (x264)");

        /// <summary>The encoders to try, in order: the chosen one (or every GPU one for Automatic), then the CPU.</summary>
        public static IReadOnlyList<(string Name, string Label)> Candidates(VideoEncoderChoice choice)
        {
            var list = new List<(string, string)>();
            foreach (var entry in Hardware)
                if (choice == VideoEncoderChoice.Automatic || choice == entry.Choice)
                    list.Add((entry.Name, entry.Label));
            list.Add((Software.Name, Software.Label));
            return list;
        }

        /// <summary>
        /// The first candidate that opens for <paramref name="width"/>×<paramref name="height"/> at <paramref name="rate"/>.
        /// <paramref name="globalHeader"/>: the container wants codec headers up front (MP4). Throws when none opens.
        /// </summary>
        public static OpenedEncoder Open(VideoEncoderChoice choice, int width, int height, FrameRate rate, int crf, bool globalHeader)
        {
            foreach (var (name, label) in Candidates(choice))
                if (TryOpen(name, label, width, height, rate, crf, globalHeader) is { } opened)
                    return opened;
            throw new InvalidOperationException("No H.264 encoder could be opened (libx264 is missing from this FFmpeg)");
        }

        private static OpenedEncoder? TryOpen(string name, string label, int width, int height, FrameRate rate, int crf, bool globalHeader)
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name(name);
            if (codec == null)
                return null;
            var opened = new OpenedEncoder { Codec = codec, Name = name, Label = label };
            try
            {
                var ctx = ffmpeg.avcodec_alloc_context3(codec);
                opened.Context = ctx;
                ctx->width = width;
                ctx->height = height;
                // the timeline's exact rate: 29.97 is 30000/1001, never rounded to 30
                ctx->time_base = new AVRational { num = rate.Den, den = rate.Num };
                ctx->framerate = new AVRational { num = rate.Num, den = rate.Den };
                ctx->gop_size = Math.Max(1, (int)Math.Round(rate.Fps * 2));
                ctx->max_b_frames = 0;
                if (globalHeader)
                    ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

                var q = Math.Clamp(crf, 0, 51).ToString(CultureInfo.InvariantCulture);
                var priv = ctx->priv_data;
                switch (name)
                {
                    case "h264_nvenc":
                        opened.InputFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
                        ffmpeg.av_opt_set(priv, "preset", "p5", 0);
                        ffmpeg.av_opt_set(priv, "rc", "vbr", 0);
                        ffmpeg.av_opt_set(priv, "cq", q, 0);
                        ctx->bit_rate = 0;
                        break;
                    case "h264_qsv":
                        opened.InputFormat = AVPixelFormat.AV_PIX_FMT_NV12;
                        ctx->global_quality = Math.Clamp(crf, 1, 51);
                        break;
                    case "h264_amf":
                        opened.InputFormat = AVPixelFormat.AV_PIX_FMT_NV12;
                        ffmpeg.av_opt_set(priv, "rc", "cqp", 0);
                        ffmpeg.av_opt_set(priv, "qp_i", q, 0);
                        ffmpeg.av_opt_set(priv, "qp_p", q, 0);
                        break;
                    case "h264_vaapi":
                        opened.InputFormat = AVPixelFormat.AV_PIX_FMT_NV12;
                        if (!AttachVaapiFrames(opened, width, height))
                            return Fail(opened);
                        ffmpeg.av_opt_set(priv, "rc_mode", "CQP", 0);
                        ffmpeg.av_opt_set(priv, "qp", q, 0);
                        break;
                    default:
                        opened.InputFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
                        ffmpeg.av_opt_set(priv, "crf", q, 0);
                        break;
                }
                if (opened.HwFrames == null)
                    ctx->pix_fmt = opened.InputFormat;

                if (ffmpeg.avcodec_open2(ctx, codec, null) < 0)
                    return Fail(opened);
                return opened;
            }
            catch
            {
                return Fail(opened);
            }
        }

        private static OpenedEncoder? Fail(OpenedEncoder opened)
        {
            opened.Dispose();
            return null;
        }

        /// <summary>VA-API encodes GPU frames: a device, and a pool of NV12 frames on it each export frame is uploaded into.</summary>
        private static bool AttachVaapiFrames(OpenedEncoder opened, int width, int height)
        {
            AVBufferRef* device = null;
            if (ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, null, null, 0) < 0)
                return false;
            opened.HwDevice = device;
            var frames = ffmpeg.av_hwframe_ctx_alloc(device);
            if (frames == null)
                return false;
            var framesCtx = (AVHWFramesContext*)frames->data;
            framesCtx->format = AVPixelFormat.AV_PIX_FMT_VAAPI;
            framesCtx->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
            framesCtx->width = width;
            framesCtx->height = height;
            framesCtx->initial_pool_size = 16;
            if (ffmpeg.av_hwframe_ctx_init(frames) < 0)
            {
                ffmpeg.av_buffer_unref(&frames);
                return false;
            }
            opened.HwFrames = frames;
            opened.Context->pix_fmt = AVPixelFormat.AV_PIX_FMT_VAAPI;
            opened.Context->hw_frames_ctx = ffmpeg.av_buffer_ref(frames);
            return true;
        }
    }
}
