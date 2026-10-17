using System.Collections.Generic;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// How video tracks stack in the picture: as the timeline shows them. A track drawn higher covers the ones below
    /// it, so the first track is the top of the picture and the last the bottom. Every compositor, hit-test and
    /// thumbnail goes through here.
    /// </summary>
    public static class TrackStacking
    {
        /// <summary>Visible video tracks with their list index, top of the picture first.</summary>
        public static IEnumerable<(int Index, Track Track)> TopDown(Timeline timeline)
        {
            for (var i = 0; i < timeline.Tracks.Count; i++)
                if (timeline.Tracks[i] is { Kind: TrackKind.Video, Visible: true } track)
                    yield return (i, track);
        }

        /// <summary>Visible video tracks with their list index, bottom of the picture first (paint order).</summary>
        public static IEnumerable<(int Index, Track Track)> BottomUp(Timeline timeline)
        {
            for (var i = timeline.Tracks.Count - 1; i >= 0; i--)
                if (timeline.Tracks[i] is { Kind: TrackKind.Video, Visible: true } track)
                    yield return (i, track);
        }

        /// <summary>Sorts layers that know their track index into paint order: the lowest track first.</summary>
        public static int PaintKey(int trackIndex) => -trackIndex;
    }
}
