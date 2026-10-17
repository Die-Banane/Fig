using System;
using FFmpeg.AutoGen;

namespace Fig.Core.Media
{
    /// <summary>
    /// The steps every reader of a media file starts with: open it, pick its best stream of a kind, open a decoder for
    /// that stream. Each throws with what failed, and frees what it made if it fails part-way; once they return, the
    /// caller owns the context (and frees it as before).
    /// </summary>
    internal static unsafe class FfmpegOpen
    {
        /// <summary>Opens a file and reads its stream info.</summary>
        public static AVFormatContext* Input(string path)
        {
            AVFormatContext* ctx = null;
            Check(ffmpeg.avformat_open_input(&ctx, path, null, null), "avformat_open_input");
            var info = ffmpeg.avformat_find_stream_info(ctx, null);
            if (info < 0)
            {
                ffmpeg.avformat_close_input(&ctx);
                Check(info, "avformat_find_stream_info");
            }
            return ctx;
        }

        /// <summary>The index of the file's best stream of <paramref name="type"/>.</summary>
        public static int BestStream(AVFormatContext* ctx, AVMediaType type)
        {
            var index = ffmpeg.av_find_best_stream(ctx, type, -1, -1, null, 0);
            Check(index, $"av_find_best_stream({(type == AVMediaType.AVMEDIA_TYPE_AUDIO ? "audio" : "video")})");
            return index;
        }

        /// <summary>
        /// A decoder opened for <paramref name="stream"/>. <paramref name="threads"/> above 0 decodes on that many threads;
        /// <paramref name="skip"/> drops frames the caller doesn't need (non-keyframes for a filmstrip).
        /// </summary>
        public static AVCodecContext* Decoder(AVStream* stream, int threads = 0, AVDiscard skip = AVDiscard.AVDISCARD_DEFAULT)
        {
            var codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (codec == null)
                throw new InvalidOperationException(stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO
                    ? "No audio decoder found" : "No video decoder found");
            var ctx = ffmpeg.avcodec_alloc_context3(codec);
            try
            {
                Check(ffmpeg.avcodec_parameters_to_context(ctx, stream->codecpar), "avcodec_parameters_to_context");
                ctx->pkt_timebase = stream->time_base;
                if (threads > 0)
                    ctx->thread_count = threads;
                ctx->skip_frame = skip;
                Check(ffmpeg.avcodec_open2(ctx, codec, null), "avcodec_open2");
                return ctx;
            }
            catch
            {
                ffmpeg.avcodec_free_context(&ctx);
                throw;
            }
        }

        private static void Check(int ret, string what)
        {
            if (ret < 0)
                throw new InvalidOperationException($"{what} failed: {ret}");
        }
    }
}
