using System;
using System.IO;
using FFmpeg.AutoGen;

namespace Fig.Core.Media
{
    public partial class MediaService
    {
        /// <summary>
        /// True when the source is large enough that a 720p proxy is worth generating.
        /// </summary>
        public static bool ShouldGenerateProxy(int width, int height, int maxHeight = 720)
            => height > maxHeight || width > 1280;

        /// <summary>
        /// Re-encodes <paramref name="sourcePath"/> to a video-only H.264 MP4 scaled to
        /// fit inside <paramref name="maxHeight"/> (even dimensions). Returns
        /// <see cref="ProxyInfo.Skipped"/> when the source is already small enough.
        /// </summary>
        public unsafe ProxyInfo GenerateProxy(string sourcePath, string outputPath, int maxHeight = 720)
        {
            AVFormatContext* inCtx = null;
            AVFormatContext* outCtx = null;
            AVCodecContext* decCtx = null;
            AVCodecContext* encCtx = null;
            SwsContext* sws = null;
            AVFrame* inFrame = null;
            AVFrame* outFrame = null;
            AVPacket* packet = null;
            var wroteFile = false;

            try
            {
                inCtx = FfmpegOpen.Input(sourcePath);

                var vIdx = FfmpegOpen.BestStream(inCtx, AVMediaType.AVMEDIA_TYPE_VIDEO);
                var inStream = inCtx->streams[vIdx];
                var inCodecPar = inStream->codecpar;

                var srcW = Math.Max(1, inCodecPar->width);
                var srcH = Math.Max(1, inCodecPar->height);
                if (!ShouldGenerateProxy(srcW, srcH, maxHeight))
                {
                    return new ProxyInfo
                    {
                        Skipped = true,
                        Width = srcW,
                        Height = srcH,
                    };
                }

                // fit inside maxHeight, preserve aspect, force even dims for yuv420
                var outH = Math.Min(maxHeight, srcH) & ~1;
                if (outH < 2) outH = 2;
                var outW = (int)Math.Round(srcW * (outH / (double)srcH)) & ~1;
                if (outW < 2) outW = 2;

                decCtx = FfmpegOpen.Decoder(inStream);

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Write to a sibling .partial path and rename only after av_write_trailer
                // puts the moov atom on disk. Readers must never open the final path mid-encode.
                var partialPath = outputPath + ".partial";
                TryDelete(partialPath);
                TryDelete(outputPath);

                FfmpegOpen.Check(ffmpeg.avformat_alloc_output_context2(&outCtx, null, "mp4", partialPath),
                    "avformat_alloc_output_context2");

                var enc = ffmpeg.avcodec_find_encoder_by_name("libx264");
                if (enc == null)
                    throw new InvalidOperationException("libx264 encoder not found");
                encCtx = ffmpeg.avcodec_alloc_context3(enc);
                encCtx->width = outW;
                encCtx->height = outH;
                encCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                // keep the source's frame rate and frame times: a proxy must be the same length as its source
                var fps = inStream->avg_frame_rate.num > 0 && inStream->avg_frame_rate.den > 0
                    ? inStream->avg_frame_rate
                    : new AVRational { num = 30, den = 1 };
                encCtx->framerate = fps;
                encCtx->time_base = new AVRational { num = fps.den, den = fps.num };
                // every frame a keyframe: any frame decodes on its own, so scrubbing is instant
                encCtx->gop_size = 1;
                encCtx->max_b_frames = 0;
                // mp4 needs global headers
                if ((outCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                    encCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

                AVDictionary* opts = null;
                ffmpeg.av_dict_set(&opts, "preset", "veryfast", 0);
                ffmpeg.av_dict_set(&opts, "tune", "fastdecode", 0);
                ffmpeg.av_dict_set(&opts, "crf", "23", 0);
                var openRet = ffmpeg.avcodec_open2(encCtx, enc, &opts);
                ffmpeg.av_dict_free(&opts);
                FfmpegOpen.Check(openRet, "avcodec_open2(libx264)");

                var outStream = ffmpeg.avformat_new_stream(outCtx, null);
                if (outStream == null)
                    throw new InvalidOperationException("avformat_new_stream failed");
                FfmpegOpen.Check(ffmpeg.avcodec_parameters_from_context(outStream->codecpar, encCtx),
                    "avcodec_parameters_from_context");
                outStream->time_base = encCtx->time_base;

                FfmpegOpen.Check(ffmpeg.avio_open(&outCtx->pb, partialPath, ffmpeg.AVIO_FLAG_WRITE), "avio_open");
                wroteFile = true;
                FfmpegOpen.Check(ffmpeg.avformat_write_header(outCtx, null), "avformat_write_header");

                inFrame = ffmpeg.av_frame_alloc();
                outFrame = ffmpeg.av_frame_alloc();
                outFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                outFrame->width = outW;
                outFrame->height = outH;
                FfmpegOpen.Check(ffmpeg.av_frame_get_buffer(outFrame, 32), "av_frame_get_buffer");
                packet = ffmpeg.av_packet_alloc();

                long outPts = 0;
                long firstTs = long.MinValue;
                AVPixelFormat swsFmt = AVPixelFormat.AV_PIX_FMT_NONE;
                var swsSrcW = 0;
                var swsSrcH = 0;

                while (ffmpeg.av_read_frame(inCtx, packet) >= 0)
                {
                    try
                    {
                        if (packet->stream_index != vIdx)
                            continue;
                        if (ffmpeg.avcodec_send_packet(decCtx, packet) < 0)
                            continue;

                        while (ffmpeg.avcodec_receive_frame(decCtx, inFrame) == 0)
                        {
                            EnsureProxySws(ref sws, ref swsFmt, ref swsSrcW, ref swsSrcH,
                                inFrame, outW, outH);
                            if (sws == null)
                                continue;

                            ffmpeg.sws_scale(sws, inFrame->data, inFrame->linesize, 0, inFrame->height,
                                outFrame->data, outFrame->linesize);
                            outPts = EncodeFrame(encCtx, outCtx, outStream, outFrame, ProxyPts(inFrame, ref firstTs, outPts, inStream->time_base, encCtx->time_base));
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(packet);
                    }
                }

                // flush decoder
                ffmpeg.avcodec_send_packet(decCtx, null);
                while (ffmpeg.avcodec_receive_frame(decCtx, inFrame) == 0)
                {
                    EnsureProxySws(ref sws, ref swsFmt, ref swsSrcW, ref swsSrcH, inFrame, outW, outH);
                    if (sws == null)
                        continue;
                    ffmpeg.sws_scale(sws, inFrame->data, inFrame->linesize, 0, inFrame->height,
                        outFrame->data, outFrame->linesize);
                    outPts = EncodeFrame(encCtx, outCtx, outStream, outFrame, ProxyPts(inFrame, ref firstTs, outPts, inStream->time_base, encCtx->time_base));
                }

                EncodeFrame(encCtx, outCtx, outStream, null, outPts);
                ffmpeg.av_write_trailer(outCtx);

                // flush/close the IO handle before rename so the final file is complete
                if (outCtx->pb != null)
                {
                    var pb = outCtx->pb;
                    ffmpeg.avio_closep(&pb);
                    outCtx->pb = null;
                }

                File.Move(partialPath, outputPath, overwrite: true);
                wroteFile = false; // final path owned by caller; don't delete on later failure

                return new ProxyInfo
                {
                    Path = outputPath,
                    Width = outW,
                    Height = outH,
                    Skipped = false,
                };
            }
            catch
            {
                if (wroteFile)
                {
                    try
                    {
                        if (outCtx != null && outCtx->pb != null)
                        {
                            var pb = outCtx->pb;
                            ffmpeg.avio_closep(&pb);
                            outCtx->pb = null;
                        }
                    }
                    catch { /* best-effort */ }

                    TryDelete(outputPath + ".partial");
                    TryDelete(outputPath);
                }
                throw;
            }
            finally
            {
                if (packet != null) ffmpeg.av_packet_free(&packet);
                if (inFrame != null) ffmpeg.av_frame_free(&inFrame);
                if (outFrame != null) ffmpeg.av_frame_free(&outFrame);
                if (sws != null) ffmpeg.sws_freeContext(sws);
                if (decCtx != null) ffmpeg.avcodec_free_context(&decCtx);
                if (encCtx != null) ffmpeg.avcodec_free_context(&encCtx);
                if (inCtx != null)
                {
                    var p = inCtx;
                    ffmpeg.avformat_close_input(&p);
                }
                if (outCtx != null)
                {
                    if (outCtx->pb != null)
                    {
                        var pb = outCtx->pb;
                        ffmpeg.avio_closep(&pb);
                        outCtx->pb = null;
                    }
                    ffmpeg.avformat_free_context(outCtx);
                }
            }
        }

        /// <summary>
        /// The frame's number on the proxy's clock, from the source frame's own timestamp (never backwards,
        /// so odd timestamps can't upset the encoder).
        /// </summary>
        private static unsafe long ProxyPts(AVFrame* frame, ref long firstTs, long minPts, AVRational srcBase, AVRational dstBase)
        {
            var ts = frame->best_effort_timestamp;
            if (ts == ffmpeg.AV_NOPTS_VALUE)
                return minPts;
            if (firstTs == long.MinValue)
                firstTs = ts;
            return Math.Max(minPts, ffmpeg.av_rescale_q(ts - firstTs, srcBase, dstBase));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* best-effort */ }
        }

        private static unsafe void EnsureProxySws(
            ref SwsContext* sws, ref AVPixelFormat swsFmt, ref int swsSrcW, ref int swsSrcH,
            AVFrame* frame, int outW, int outH)
        {
            var fmt = (AVPixelFormat)frame->format;
            var fw = frame->width;
            var fh = frame->height;
            if (fmt == AVPixelFormat.AV_PIX_FMT_NONE || fw <= 0 || fh <= 0)
                return;
            if (sws != null && swsFmt == fmt && swsSrcW == fw && swsSrcH == fh)
                return;
            if (sws != null)
                ffmpeg.sws_freeContext(sws);
            sws = ffmpeg.sws_getContext(fw, fh, fmt, outW, outH,
                AVPixelFormat.AV_PIX_FMT_YUV420P, SWS_BILINEAR, null, null, null);
            swsFmt = fmt;
            swsSrcW = fw;
            swsSrcH = fh;
        }
    }
}
