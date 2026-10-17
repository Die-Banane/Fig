using System;
using System.Linq;
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

        /// <summary>Height of a text clip: a title has no picture or name strip, just its words, so its bar is thin.</summary>
        public const double TextClipHeight = 26.0;

        /// <summary>
        /// True when a track holds only text. Clips in a lane share one height, so such a track is thin, and one
        /// with any picture in it keeps the full height.
        /// </summary>
        public static bool IsTextOnly(Track track) =>
            track.Kind != TrackKind.Audio && track.Clips.Count > 0 && track.Clips.All(c => c is TextClip);

        /// <summary>Height of the name strip above the clips' bodies in a track (none on a text-only track).</summary>
        public static double LabelHeight(Track track) => IsTextOnly(track) ? 0 : ClipLabelHeight;

        public static double BodyHeight(Track track) => track.Kind == TrackKind.Audio ? AudioClipHeight
            : IsTextOnly(track) ? TextClipHeight
            : ClipHeight;

        public static double ClipTotal(Track track) => LabelHeight(track) + BodyHeight(track);

        public static double LaneHeight(Track track) => ClipInset + ClipTotal(track) + LaneBottomPadding;

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
