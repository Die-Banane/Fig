using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Tracks: adding, ordering, removing, showing, muting and naming them.
    /// </summary>
    public partial class TimelineEditor
    {
        public Track AddTrack(TrackKind kind)
        {
            var track = new Track
            {
                Kind = kind,
                Name = kind == TrackKind.Video ? $"V{CountTracks(TrackKind.Video) + 1}" : $"A{CountTracks(TrackKind.Audio) + 1}",
            };
            var before = Document.Tracks.ToList();
            History.Execute(new SetTrackListCommand(this, "Add track", before, WithNewTrack(before, track)));
            RaiseChanged();
            return track;
        }

        /// <summary>
        /// The track list with <paramref name="track"/> added: a video track on top of the picture (above the other
        /// video tracks, see <see cref="TrackStacking"/>), an audio track at the end.
        /// </summary>
        private static List<Track> WithNewTrack(IReadOnlyList<Track> tracks, Track track)
        {
            var after = tracks.ToList();
            var topVideo = after.FindIndex(t => t.Kind == TrackKind.Video);
            if (track.Kind == TrackKind.Video && topVideo >= 0)
                after.Insert(topVideo, track);
            else
                after.Add(track);
            return after;
        }

        /// <summary>
        /// Moves a track so it sits before the track now at <paramref name="insertIndex"/> (0 is the top; the track
        /// count is the end). Higher video tracks cover lower ones, so this changes the picture's stacking. One undo step.
        /// </summary>
        public bool MoveTrack(string trackId, int insertIndex)
        {
            var before = Document.Tracks.ToList();
            var from = before.FindIndex(t => t.Id == trackId);
            if (from < 0)
                return false;
            insertIndex = Math.Clamp(insertIndex, 0, before.Count);
            var to = insertIndex > from ? insertIndex - 1 : insertIndex;
            if (to == from)
                return false;
            var after = before.ToList();
            var track = after[from];
            after.RemoveAt(from);
            after.Insert(to, track);
            History.Execute(new SetTrackListCommand(this, "Move track", before, after));
            RaiseChanged();
            return true;
        }

        public bool RemoveTrack(string trackId)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;

            var before = Document.Tracks.ToList();
            var after = before.Where(t => t.Id != trackId).ToList();
            History.Execute(new SetTrackListCommand(this, "Delete track", before, after));
            Selection.Clear();
            RaiseChanged();
            return true;
        }

        /// <summary>Shows or hides a video track (undoable; the preview and autosave follow).</summary>
        public void SetTrackVisible(string trackId, bool visible)
        {
            if (FindTrack(trackId) is not { } track || track.Visible == visible)
                return;
            History.Execute(new SetTrackFlagCommand(track, muted: false, value: visible));
            RaiseChanged();
        }

        /// <summary>Mutes or unmutes an audio track (undoable).</summary>
        public void SetTrackMuted(string trackId, bool muted)
        {
            if (FindTrack(trackId) is not { } track || track.Muted == muted)
                return;
            History.Execute(new SetTrackFlagCommand(track, muted: true, value: muted));
            RaiseChanged();
        }

        /// <summary>Renames a track (an empty name goes back to the automatic V1 / A1 style label).</summary>
        public void RenameTrack(string trackId, string? name)
        {
            if (FindTrack(trackId) is not { } track)
                return;
            History.Execute(new RenameTrackCommand(track, name));
            RaiseChanged();
        }

        private int CountTracks(TrackKind kind)
        {
            var count = 0;
            foreach (var track in Document.Tracks)
                if (track.Kind == kind)
                    count++;
            return count;
        }

        private void ReindexTracks()
        {
            var v = 0;
            var a = 0;
            for (var i = 0; i < Document.Tracks.Count; i++)
            {
                var track = Document.Tracks[i];
                track.Index = i;
                if (track.Kind == TrackKind.Video)
                    track.Name = $"V{++v}";
                else
                    track.Name = $"A{++a}";
            }
        }

        /// <summary>
        /// Re-syncs each track's <see cref="Track.Index"/> (and name) with its position in the
        /// list. Called on project load because Index is serialized and can be stale.
        /// </summary>
        public void RefreshTrackIndices()
        {
            ReindexTracks();
        }

        public Track EnsureTrack(TrackKind kind)
        {
            foreach (var track in Document.Tracks)
                if (track.Kind == kind)
                    return track;
            return AddTrack(kind);
        }

        public Track FindTrackForClip(Clip clip)
        {
            return FindClipTrack(clip.Id) ?? EnsureTrack(clip.Kind switch
            {
                ClipKind.Audio => TrackKind.Audio,
                _ => TrackKind.Video,
            });
        }
    }
}
