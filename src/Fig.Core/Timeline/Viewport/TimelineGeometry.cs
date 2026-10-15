using System;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    public static class TimelineGeometry
    {
        public const double DefaultPixelsPerSecond = 100.0;

        /// <summary>Gap above a clip inside its lane (room for marker heads).</summary>
        public const double ClipInset = 6.0;
        public const double ClipLabelHeight = 14.0;
        public const double LaneBottomPadding = 8.0;

        /// <summary>Height of the picture strip of a video clip.</summary>
        public const double ClipHeight = 72.0;

        /// <summary>Height of the waveform strip of an audio clip.</summary>
        public const double AudioClipHeight = 52.0;

        /// <summary>Total height of a video clip widget including its label strip.</summary>
        public const double ClipTotalHeight = ClipLabelHeight + ClipHeight;

        /// <summary>Height of a video lane; also the default for fixed-height helpers.</summary>
        public const double TrackHeight = ClipInset + ClipTotalHeight + LaneBottomPadding;

        /// <summary>Height of an audio lane.</summary>
        public const double AudioTrackHeight = ClipInset + ClipLabelHeight + AudioClipHeight + LaneBottomPadding;

        public static double BodyHeight(TrackKind kind) => kind == TrackKind.Audio ? AudioClipHeight : ClipHeight;

        public static double ClipTotal(TrackKind kind) => ClipLabelHeight + BodyHeight(kind);

        public static double LaneHeight(TrackKind kind) => kind == TrackKind.Audio ? AudioTrackHeight : TrackHeight;

        public static double TimeToX(double seconds, double pxPerSec = DefaultPixelsPerSecond)
        {
            return seconds * pxPerSec;
        }

        public static double XToTime(double x, double pxPerSec = DefaultPixelsPerSecond)
        {
            return x / pxPerSec;
        }

        public static double ClipX(Clip clip, double pxPerSec = DefaultPixelsPerSecond)
        {
            return TimeToX(clip.StartSec, pxPerSec);
        }

        public static double ClipWidth(Clip clip, double pxPerSec = DefaultPixelsPerSecond)
        {
            return clip.DurSec * pxPerSec;
        }

        public static double TrackTop(int trackIndex, double trackHeight = TrackHeight)
        {
            return trackIndex * trackHeight;
        }

        public static double TrackBottom(int trackIndex, double trackHeight = TrackHeight)
        {
            return TrackTop(trackIndex + 1, trackHeight);
        }
    }
}
