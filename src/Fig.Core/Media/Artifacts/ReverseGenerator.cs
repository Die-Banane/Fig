using System;
using System.Collections.Generic;
using System.IO;
using FFmpeg.AutoGen;

namespace Fig.Core.Media
{
    public partial class MediaService
    {
        /// <summary>The most decoded frames held at once while reversing (they are written out in reverse).</summary>
        private const long ReverseFrameBudgetBytes = 192L * 1024 * 1024;

        /// <summary>Audio is reversed in pieces this long.</summary>
        private const double ReverseAudioChunkSec = 1.0;

        /// <summary>
        /// Writes <paramref name="sourcePath"/> played backwards to <paramref name="outputPath"/>: the last frame first,
        /// the sound reversed with it (H.264 and AAC in MP4; sound only, as M4A, for an audio file). Time
        /// <c>t</c> in the copy is time <c>length - t</c> in the source. The source is decoded in chunks from its end,
        /// so memory stays bounded however long it is. <paramref name="progress"/> runs from 0 to 1.
        /// </summary>
        public unsafe void GenerateReversed(string sourcePath, string outputPath, Action<double>? progress = null)
        {
            AVFormatContext* inCtx = null;
            AVFormatContext* outCtx = null;
            AVCodecContext* decCtx = null;
            AVCodecContext* vEnc = null;
            AVCodecContext* aEnc = null;
            SwsContext* sws = null;
            AVFrame* decoded = null;
            AVPacket* packet = null;
            AudioSampleSource? audio = null;
            var held = new List<IntPtr>();
            var partialPath = outputPath + ".partial";

            try
            {
                inCtx = FfmpegOpen.Input(sourcePath);
                var vIdx = ffmpeg.av_find_best_stream(inCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
                var hasAudio = ffmpeg.av_find_best_stream(inCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0) >= 0;
                if (vIdx < 0 && !hasAudio)
                    throw new InvalidOperationException("Nothing to reverse: no video or audio");

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                TryDelete(partialPath);
                FfmpegOpen.Check(ffmpeg.avformat_alloc_output_context2(&outCtx, null, vIdx >= 0 ? "mp4" : "ipod", partialPath),
                    "avformat_alloc_output_context2");
                var globalHeader = (outCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0;

                // ---- video: every frame's time, read from the packets (no decoding) ----
                AVStream* inVideo = null, outVideo = null;
                var frameTimes = new List<long>();
                var fps = new AVRational { num = 30, den = 1 };
                int width = 0, height = 0;
                if (vIdx >= 0)
                {
                    inVideo = inCtx->streams[vIdx];
                    if (inVideo->avg_frame_rate.num > 0 && inVideo->avg_frame_rate.den > 0)
                        fps = inVideo->avg_frame_rate;
                    packet = ffmpeg.av_packet_alloc();
                    while (ffmpeg.av_read_frame(inCtx, packet) >= 0)
                    {
                        if (packet->stream_index == vIdx)
                        {
                            var ts = packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts;
                            if (ts != ffmpeg.AV_NOPTS_VALUE)
                                frameTimes.Add(ts);
                        }
                        ffmpeg.av_packet_unref(packet);
                    }
                    frameTimes.Sort();
                    frameTimes = DistinctSorted(frameTimes);
                    if (frameTimes.Count == 0)
                        throw new InvalidOperationException("The video has no frames");

                    decCtx = FfmpegOpen.Decoder(inVideo);
                    width = Math.Max(2, inVideo->codecpar->width & ~1);
                    height = Math.Max(2, inVideo->codecpar->height & ~1);
                    vEnc = OpenReverseVideoEncoder(width, height, fps, globalHeader);
                    outVideo = ffmpeg.avformat_new_stream(outCtx, null);
                    FfmpegOpen.Check(ffmpeg.avcodec_parameters_from_context(outVideo->codecpar, vEnc), "avcodec_parameters_from_context (video)");
                    outVideo->time_base = vEnc->time_base;
                    outVideo->sample_aspect_ratio = inVideo->sample_aspect_ratio;
                    outVideo->codecpar->sample_aspect_ratio = inVideo->codecpar->sample_aspect_ratio;
                    CopyDisplayMatrix(inVideo, outVideo);
                }

                // ---- audio: AAC, stereo, at the mixer's rate ----
                AVStream* outAudio = null;
                if (hasAudio)
                {
                    aEnc = OpenReverseAudioEncoder(globalHeader);
                    outAudio = ffmpeg.avformat_new_stream(outCtx, null);
                    FfmpegOpen.Check(ffmpeg.avcodec_parameters_from_context(outAudio->codecpar, aEnc), "avcodec_parameters_from_context (audio)");
                    outAudio->time_base = aEnc->time_base;
                    audio = new AudioSampleSource(sourcePath, Audio.AudioMixer.SampleRate);
                }

                FfmpegOpen.Check(ffmpeg.avio_open(&outCtx->pb, partialPath, ffmpeg.AVIO_FLAG_WRITE), "avio_open");
                FfmpegOpen.Check(ffmpeg.avformat_write_header(outCtx, null), "avformat_write_header");

                // the copy's length: the frames at the source's rate, or the sound's for an audio file
                var frameSec = fps.den / (double)fps.num;
                var length = vIdx >= 0 ? frameTimes.Count * frameSec : Math.Max(0, MeasureDurationSec(sourcePath));
                var audioOut = new ReversedAudioWriter(aEnc, outCtx, outAudio, audio, length);

                if (vIdx >= 0)
                {
                    decoded = ffmpeg.av_frame_alloc();
                    var frameBytes = (long)width * height * 3 / 2;
                    var perChunk = (int)Math.Clamp(ReverseFrameBudgetBytes / Math.Max(1, frameBytes), 4, 240);
                    long outIndex = 0;
                    for (var end = frameTimes.Count; end > 0; end -= perChunk)
                    {
                        var start = Math.Max(0, end - perChunk);
                        DecodeChunk(inCtx, decCtx, packet, decoded, vIdx, frameTimes[start], frameTimes[end - 1],
                            width, height, ref sws, held);
                        // newest first: the chunk's last frame is the copy's next one
                        for (var i = held.Count - 1; i >= 0; i--)
                        {
                            var frame = (AVFrame*)held[i];
                            frame->pts = outIndex++;
                            WriteEncoded(vEnc, outCtx, outVideo, frame);
                        }
                        FreeFrames(held);
                        audioOut.WriteUntil(outIndex * frameSec);
                        progress?.Invoke(outIndex / (double)frameTimes.Count);
                    }
                    WriteEncoded(vEnc, outCtx, outVideo, null);
                }
                for (var t = ReverseAudioChunkSec; ; t += ReverseAudioChunkSec)
                {
                    audioOut.WriteUntil(Math.Min(t, length));
                    if (vIdx < 0)
                        progress?.Invoke(Math.Min(1, t / Math.Max(1e-6, length)));
                    if (t >= length)
                        break;
                }
                audioOut.Finish();

                FfmpegOpen.Check(ffmpeg.av_write_trailer(outCtx), "av_write_trailer");
                var pb = outCtx->pb;
                ffmpeg.avio_closep(&pb);
                outCtx->pb = null;
                File.Move(partialPath, outputPath, overwrite: true);
                progress?.Invoke(1);
            }
            catch
            {
                if (outCtx != null && outCtx->pb != null)
                {
                    var pb = outCtx->pb;
                    ffmpeg.avio_closep(&pb);
                    outCtx->pb = null;
                }
                TryDelete(partialPath);
                throw;
            }
            finally
            {
                FreeFrames(held);
                audio?.Dispose();
                if (packet != null) ffmpeg.av_packet_free(&packet);
                if (decoded != null) ffmpeg.av_frame_free(&decoded);
                if (sws != null) ffmpeg.sws_freeContext(sws);
                if (decCtx != null) ffmpeg.avcodec_free_context(&decCtx);
                if (vEnc != null) ffmpeg.avcodec_free_context(&vEnc);
                if (aEnc != null) ffmpeg.avcodec_free_context(&aEnc);
                if (inCtx != null) ffmpeg.avformat_close_input(&inCtx);
                if (outCtx != null)
                {
                    if (outCtx->pb != null)
                    {
                        var pb = outCtx->pb;
                        ffmpeg.avio_closep(&pb);
                    }
                    ffmpeg.avformat_free_context(outCtx);
                }
            }
        }

        private static List<long> DistinctSorted(List<long> sorted)
        {
            var result = new List<long>(sorted.Count);
            foreach (var t in sorted)
                if (result.Count == 0 || result[^1] != t)
                    result.Add(t);
            return result;
        }

        /// <summary>
        /// Decodes the frames timed <paramref name="first"/> to <paramref name="last"/> (stream time) into
        /// <paramref name="held"/>, in order, each converted to the encoder's YUV 4:2:0 at the output size.
        /// </summary>
        private static unsafe void DecodeChunk(AVFormatContext* inCtx, AVCodecContext* decCtx, AVPacket* packet, AVFrame* decoded,
            int vIdx, long first, long last, int width, int height, ref SwsContext* sws, List<IntPtr> held)
        {
            ffmpeg.av_seek_frame(inCtx, vIdx, first, ffmpeg.AVSEEK_FLAG_BACKWARD);
            ffmpeg.avcodec_flush_buffers(decCtx);
            var done = false;
            var eof = false;
            while (!done)
            {
                if (!eof)
                {
                    if (ffmpeg.av_read_frame(inCtx, packet) < 0)
                    {
                        eof = true;
                        ffmpeg.avcodec_send_packet(decCtx, null);
                    }
                    else
                    {
                        var ours = packet->stream_index == vIdx;
                        if (ours)
                            ffmpeg.avcodec_send_packet(decCtx, packet);
                        ffmpeg.av_packet_unref(packet);
                        if (!ours)
                            continue;
                    }
                }

                int received;
                while ((received = ffmpeg.avcodec_receive_frame(decCtx, decoded)) == 0)
                {
                    var ts = decoded->best_effort_timestamp;
                    if (ts != ffmpeg.AV_NOPTS_VALUE && ts >= first && ts <= last)
                        held.Add((IntPtr)ToEncoderFrame(decoded, width, height, ref sws));
                    ffmpeg.av_frame_unref(decoded);
                    if (ts != ffmpeg.AV_NOPTS_VALUE && ts >= last)
                        done = true;
                }
                if (eof && received == ffmpeg.AVERROR_EOF)
                    done = true;
            }
        }

        private static unsafe AVFrame* ToEncoderFrame(AVFrame* source, int width, int height, ref SwsContext* sws)
        {
            sws = ffmpeg.sws_getCachedContext(sws, source->width, source->height, (AVPixelFormat)source->format,
                width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, SWS_BILINEAR, null, null, null);
            if (sws == null)
                throw new InvalidOperationException("sws_getCachedContext failed");
            var frame = ffmpeg.av_frame_alloc();
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;
            FfmpegOpen.Check(ffmpeg.av_frame_get_buffer(frame, 32), "av_frame_get_buffer");
            ffmpeg.sws_scale(sws, source->data, source->linesize, 0, source->height, frame->data, frame->linesize);
            return frame;
        }

        private static unsafe void FreeFrames(List<IntPtr> frames)
        {
            foreach (var p in frames)
            {
                var frame = (AVFrame*)p;
                ffmpeg.av_frame_free(&frame);
            }
            frames.Clear();
        }

        /// <summary>A reversed copy is edited like footage: good quality, and a keyframe every half second so seeking stays quick.</summary>
        private static unsafe AVCodecContext* OpenReverseVideoEncoder(int width, int height, AVRational fps, bool globalHeader)
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (codec == null)
                throw new InvalidOperationException("libx264 encoder not found");
            var enc = ffmpeg.avcodec_alloc_context3(codec);
            enc->width = width;
            enc->height = height;
            enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            enc->framerate = fps;
            enc->time_base = new AVRational { num = fps.den, den = fps.num };
            enc->gop_size = Math.Max(1, (int)Math.Round(fps.num / (double)fps.den / 2));
            enc->max_b_frames = 0;
            if (globalHeader)
                enc->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            AVDictionary* opts = null;
            ffmpeg.av_dict_set(&opts, "preset", "veryfast", 0);
            ffmpeg.av_dict_set(&opts, "crf", "18", 0);
            var ret = ffmpeg.avcodec_open2(enc, codec, &opts);
            ffmpeg.av_dict_free(&opts);
            if (ret < 0)
            {
                ffmpeg.avcodec_free_context(&enc);
                FfmpegOpen.Check(ret, "avcodec_open2(libx264)");
            }
            return enc;
        }

        private static unsafe AVCodecContext* OpenReverseAudioEncoder(bool globalHeader)
        {
            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (codec == null)
                throw new InvalidOperationException("AAC encoder not found");
            var enc = ffmpeg.avcodec_alloc_context3(codec);
            enc->sample_rate = Audio.AudioMixer.SampleRate;
            enc->bit_rate = 192000;
            AVChannelLayout stereo = default;
            ffmpeg.av_channel_layout_default(&stereo, 2);
            enc->ch_layout = stereo;
            enc->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            enc->time_base = new AVRational { num = 1, den = Audio.AudioMixer.SampleRate };
            if (globalHeader)
                enc->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            var ret = ffmpeg.avcodec_open2(enc, codec, null);
            if (ret < 0)
            {
                ffmpeg.avcodec_free_context(&enc);
                FfmpegOpen.Check(ret, "avcodec_open2 (audio)");
            }
            return enc;
        }

        /// <summary>A phone video's rotation travels with its picture, so the copy turns the same way.</summary>
        private static unsafe void CopyDisplayMatrix(AVStream* from, AVStream* to)
        {
            var side = ffmpeg.av_packet_side_data_get(from->codecpar->coded_side_data, from->codecpar->nb_coded_side_data,
                AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX);
            if (side == null)
                return;
            var copy = ffmpeg.av_packet_side_data_new(&to->codecpar->coded_side_data, &to->codecpar->nb_coded_side_data,
                AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, side->size, 0);
            if (copy != null)
                Buffer.MemoryCopy(side->data, copy->data, (long)side->size, (long)side->size);
        }

        /// <summary>Encodes one frame (null: flush) and writes what comes out to its stream.</summary>
        private static unsafe void WriteEncoded(AVCodecContext* enc, AVFormatContext* outCtx, AVStream* stream, AVFrame* frame)
        {
            ffmpeg.avcodec_send_frame(enc, frame);
            var pkt = ffmpeg.av_packet_alloc();
            try
            {
                while (ffmpeg.avcodec_receive_packet(enc, pkt) == 0)
                {
                    pkt->stream_index = stream->index;
                    ffmpeg.av_packet_rescale_ts(pkt, enc->time_base, stream->time_base);
                    ffmpeg.av_interleaved_write_frame(outCtx, pkt);
                }
            }
            finally
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        /// <summary>
        /// The reversed sound, written as far as the video has got: copy time <c>t</c> is source time
        /// <c>length - t</c>, read a piece at a time from the end and played backwards. Does nothing without audio.
        /// </summary>
        private sealed unsafe class ReversedAudioWriter(AVCodecContext* enc, AVFormatContext* outCtx, AVStream* stream,
            AudioSampleSource? source, double length)
        {
            private readonly List<float> _pending = new();   // interleaved stereo, not yet a whole encoder frame
            private long _written;                           // sample frames read so far
            private long _pts;

            public void WriteUntil(double copySec)
            {
                if (source is null)
                    return;
                var target = (long)Math.Round(Math.Min(copySec, length) * Audio.AudioMixer.SampleRate);
                if (target <= _written)
                    return;
                var from = length - target / (double)Audio.AudioMixer.SampleRate;
                var samples = source.Read(Math.Max(0, from), (target - _written) / (double)Audio.AudioMixer.SampleRate);
                // backwards, keeping each left/right pair together
                for (var i = samples.Length / 2 - 1; i >= 0; i--)
                {
                    _pending.Add(samples[i * 2]);
                    _pending.Add(samples[i * 2 + 1]);
                }
                _written = target;
                Drain(final: false);
            }

            public void Finish()
            {
                if (source is null)
                    return;
                Drain(final: true);
                WriteEncoded(enc, outCtx, stream, null);
            }

            private void Drain(bool final)
            {
                var size = enc->frame_size > 0 ? enc->frame_size : 1024;
                while (_pending.Count / 2 >= size || (final && _pending.Count > 0))
                {
                    var n = Math.Min(size, _pending.Count / 2);
                    var frame = ffmpeg.av_frame_alloc();
                    try
                    {
                        frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
                        frame->nb_samples = n;
                        AVChannelLayout stereo = default;
                        ffmpeg.av_channel_layout_default(&stereo, 2);
                        frame->ch_layout = stereo;
                        FfmpegOpen.Check(ffmpeg.av_frame_get_buffer(frame, 0), "av_frame_get_buffer (audio)");
                        for (var c = 0; c < 2; c++)
                        {
                            var dst = (float*)frame->extended_data[c];
                            for (var i = 0; i < n; i++)
                                dst[i] = _pending[i * 2 + c];
                        }
                        frame->pts = _pts;
                        _pts += n;
                        WriteEncoded(enc, outCtx, stream, frame);
                    }
                    finally
                    {
                        ffmpeg.av_frame_free(&frame);
                    }
                    _pending.RemoveRange(0, n * 2);
                }
            }
        }
    }
}
