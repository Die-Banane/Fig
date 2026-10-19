using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Putting media on the timeline, and moving clips between tracks without overlaps.
    /// </summary>
    public partial class TimelineEditor
    {
        public void AddClip(string trackId, Clip clip)
        {
            var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' not found");
            InsertClip(track, clip);
            RaiseChanged();
        }

        /// <summary>
        /// Adds a media asset to the timeline as a clip. A video's sound is part of its clip (see
        /// <see cref="VideoClip.PlaysAudio"/>) until it is detached.
        /// Returns null only when the drop would overlap an existing clip on the targeted
        /// track of the matching kind.
        /// </summary>
        public Clip? AddMediaLinked(MediaAsset asset, string targetTrackId, double startSec)
        {
            var target = FindTrack(targetTrackId)
                ?? throw new InvalidOperationException($"Track '{targetTrackId}' not found");

            var clip = CreateClipFromAsset(asset);
            PreparePlacement(clip, asset);
            clip.StartSec = FrameMath.SnapToFrame(startSec, Document.Rate);
            ClipFactory.SetSourceRange(clip, 0, asset.DurationSec);

            var desiredKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;

            // drop on a matching-kind track: honor that target and reject on overlap.
            // drop on the wrong kind (e.g. video onto an audio lane): place on a free
            // matching track instead of inserting the wrong clip type.
            Track placeTrack;
            if (target.Kind == desiredKind)
            {
                if (WouldOverlap(target.Id, clip.StartSec, clip.DurSec))
                    return null;
                placeTrack = target;
            }
            else
            {
                placeTrack = FindFreeTrack(desiredKind, clip, current: null)!;
            }

            InsertClip(placeTrack, clip);
            RaiseChanged();
            return clip;
        }

        /// <summary>
        /// Adds a media asset to a brand-new track of its kind. Used when dropping into empty space so
        /// the new clip gets its own track instead of sharing the one above.
        /// </summary>
        public Clip AddMediaNewTracks(MediaAsset asset, double startSec)
        {
            var clip = CreateClipFromAsset(asset);
            PreparePlacement(clip, asset);
            clip.StartSec = FrameMath.SnapToFrame(startSec, Document.Rate);
            ClipFactory.SetSourceRange(clip, 0, asset.DurationSec);

            if (asset.Kind == MediaKind.Audio)
            {
                var audioTrack = AddTrack(TrackKind.Audio);
                InsertClip(audioTrack, clip);
                RaiseChanged();
                return clip;
            }

            var videoTrack = AddTrack(TrackKind.Video);

            InsertClip(videoTrack, clip);
            RaiseChanged();
            return clip;
        }

        /// <summary>Returns all clips sharing the given clip's link group (including itself).</summary>
        public IReadOnlyList<Clip> LinkGroup(string clipId)
        {
            var clip = FindClip(clipId);
            if (clip is null)
                return Array.Empty<Clip>();
            // treat empty the same as null so unlinked clips never form a phantom group
            if (string.IsNullOrEmpty(clip.LinkGroupId))
                return new[] { clip };

            var group = new List<Clip>();
            foreach (var track in Document.Tracks)
                foreach (var c in track.Clips)
                    if (c.LinkGroupId == clip.LinkGroupId)
                        group.Add(c);
            return group;
        }

        public static Clip CreateClipFromAsset(MediaAsset asset)
        {
            return asset.Kind switch
            {
                MediaKind.Audio => new AudioClip { SourceId = asset.Id, DurSec = asset.DurationSec, SourceRate = asset.SourceRate },
                // a still lasts the default length; an animated one (a GIF) at least one whole loop
                MediaKind.Image => new ImageClip { SourceId = asset.Id, DurSec = Math.Max(ImageClip.DefaultDurationSec, asset.DurationSec) },
                _ => new VideoClip { SourceId = asset.Id, DurSec = asset.DurationSec, SourceRate = asset.SourceRate, PlaysAudio = asset.HasAudio },
            };
        }

        /// <summary>
        /// Readies a newly created clip for the timeline. An empty timeline whose rate the user hasn't chosen takes
        /// the media's frame rate, if it reports a sensible one. The clip plays in real time, as long as its file,
        /// whatever its frame rate.
        /// </summary>
        private Clip PreparePlacement(Clip clip, MediaAsset asset)
        {
            clip.SourceRate = asset.SourceRate;
            if (!Document.RateChosen && HasNoClips() && FrameRate.Plausible(asset.SourceRate) is { } rate)
                Document.Rate = rate;
            return clip;
        }

        /// <summary>Sets the project frame rate, one undo step. Clips keep their place and length.</summary>
        public void SetRate(FrameRate rate)
        {
            if (rate.Equals(Document.Rate) && Document.RateChosen)
                return;
            History.Execute(new SetTimelineRateCommand(this, rate));
            RaiseChanged();
        }

        public void SetFrameShape(FrameShape shape)
        {
            if (shape == Document.Shape)
                return;
            History.Execute(new SetFrameShapeCommand(this, shape));
            RaiseChanged();
        }

        private bool HasNoClips()
        {
            foreach (var track in Document.Tracks)
                if (track.Clips.Count > 0)
                    return false;
            return true;
        }

        public void AddClip(Clip clip)
        {
            var track = FindTrackForClip(clip);
            InsertClip(track, clip);
            RaiseChanged();
        }

        /// <summary>
        /// Moves dragged clips live: those of the target's kind (all from one track) onto <paramref name="targetTrackId"/>,
        /// every clip to its start in <paramref name="starts"/>. Clips of the other kind (a linked partner) stay on their
        /// own tracks and only move in time. Nothing changes, and false comes back, when anything would land on another
        /// clip. Not an undo step by itself: the drag records one when it ends (<see cref="CommitClipGeometry"/>).
        /// </summary>
        public bool MoveClipsToTrack(IReadOnlyCollection<string> clipIds, string targetTrackId, IReadOnlyDictionary<string, double> starts)
        {
            if (FindTrack(targetTrackId) is not { } target)
                return false;
            var clips = clipIds.Distinct().Select(FindClip).Where(c => c is not null).Cast<Clip>().ToList();
            var movers = clips.Where(c => (c.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Video) == target.Kind).ToList();
            if (movers.Count == 0 || movers.Select(c => FindClipTrack(c.Id)?.Id).Distinct().Count() != 1)
                return false;   // only clips from one track move across together
            var moving = clips.Select(c => c.Id).ToHashSet();
            double StartOf(Clip c) => starts.TryGetValue(c.Id, out var s) ? Math.Max(0, s) : c.StartSec;
            bool Free(Track track, Clip clip)
                => !track.Clips.Any(o => !moving.Contains(o.Id)
                    && o.StartSec < StartOf(clip) + clip.DurSec - 1e-9 && StartOf(clip) < o.StartSec + o.DurSec - 1e-9);

            if (movers.Any(c => !Free(target, c)))
                return false;
            foreach (var other in clips.Except(movers))
                if (FindClipTrack(other.Id) is { } own && !Free(own, other))
                    return false;

            foreach (var clip in clips)
                clip.StartSec = StartOf(clip);
            foreach (var clip in movers)
                if (FindClipTrack(clip.Id) is { } from && from.Id != target.Id)
                {
                    from.Clips.Remove(clip);
                    InsertClip(target, clip);
                }
            RefreshTrackIndices();
            return true;
        }

        /// <summary>Finds a track of <paramref name="kind"/> where <paramref name="clip"/> fits without overlap, else null.</summary>
        private Track? FindFreeTrack(TrackKind kind, Clip clip, Track? current)
        {
            foreach (var track in Document.Tracks)
            {
                if (track.Kind != kind)
                    continue;
                if (current is not null && track.Id == current.Id)
                    continue;
                if (WouldOverlap(track.Id, clip.StartSec, clip.DurSec, clip.Id))
                    continue;
                return track;
            }
            // no free track of this kind -> create one
            return AddTrack(kind);
        }

        /// <summary>True when placing a clip of <paramref name="durSec"/> at <paramref name="startSec"/> on a track would overlap another clip.</summary>
        public bool WouldOverlap(string trackId, double startSec, double durSec, string? excludeClipId = null)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;
            var end = startSec + durSec;
            foreach (var clip in track.Clips)
            {
                if (excludeClipId is not null && clip.Id == excludeClipId)
                    continue;
                if (clip.StartSec < end && clip.StartSec + clip.DurSec > startSec)
                    return true;
            }
            return false;
        }

        /// <summary>Returns the id of the track holding <paramref name="clipId"/>, or null.</summary>
        public string? FindClipTrackId(string clipId)
        {
            return FindClipTrack(clipId)?.Id;
        }

        /// <summary>
        /// True when moving a link group to <paramref name="startSec"/> on <paramref name="trackId"/>
        /// would overlap a clip outside the group. Assumes all group members share the same start.
        /// </summary>
        public bool WouldOverlapGroup(string trackId, IReadOnlyCollection<string> groupIds, double startSec, double durSec)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;
            var end = startSec + durSec;
            foreach (var clip in track.Clips)
            {
                if (groupIds.Contains(clip.Id))
                    continue;
                if (clip.StartSec < end && clip.StartSec + clip.DurSec > startSec)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when moving the given clips to the given start times would make any of them overlap a clip
        /// that is not moving with them (on the track each currently sits on).
        /// </summary>
        public bool WouldOverlapMove(IReadOnlyDictionary<string, double> newStarts)
        {
            foreach (var (id, start) in newStarts)
            {
                var clip = FindClip(id);
                var track = FindClipTrack(id);
                if (clip is null || track is null)
                    continue;
                var end = start + clip.DurSec;
                foreach (var other in track.Clips)
                {
                    if (newStarts.ContainsKey(other.Id))
                        continue;
                    if (other.StartSec < end - 1e-9 && other.StartSec + other.DurSec > start + 1e-9)
                        return true;
                }
            }
            return false;
        }
    }
}
