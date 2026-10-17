using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Timeline;

namespace Fig.Core.Project
{
    /// <summary>
    /// Upgrades a project saved when a video's sound was a separate audio clip linked to it. A video clip and its
    /// linked audio clip that play the same part of the same file at the same place become one clip that
    /// <see cref="VideoClip.PlaysAudio"/>; the audio clip's level, fades, volume keyframes and markers move onto it.
    /// A pair that has drifted apart (moved or trimmed on its own) stays as it is, the audio clip playing
    /// the sound, and every other video clip stays silent, so the project sounds as it did.
    /// </summary>
    public static class EmbeddedAudioMigration
    {
        public const int Version = 1;

        private const double Eps = 1e-6;

        /// <summary>Upgrades the project if it is older than <see cref="Version"/>. Returns how many pairs were merged.</summary>
        public static int Migrate(Project project)
        {
            if (project.FormatVersion >= Version)
                return 0;
            var merged = 0;
            foreach (var timeline in project.Timelines)
                merged += Migrate(timeline);
            project.FormatVersion = Version;
            return merged;
        }

        private static int Migrate(Fig.Core.Timeline.Timeline timeline)
        {
            var audioTracks = timeline.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
            var emptied = new HashSet<Track>();
            var merged = 0;

            foreach (var video in timeline.Tracks.Where(t => t.Kind == TrackKind.Video).SelectMany(t => t.Clips).OfType<VideoClip>())
            {
                if (string.IsNullOrEmpty(video.LinkGroupId))
                    continue;
                var match = audioTracks
                    .SelectMany(track => track.Clips.OfType<AudioClip>().Select(audio => (track, audio)))
                    .FirstOrDefault(pair => pair.audio.LinkGroupId == video.LinkGroupId && SameSound(video, pair.audio));
                if (match.audio is not { } audio)
                    continue;

                video.PlaysAudio = true;
                video.Volume = audio.Volume;
                video.AudioFadeInSec = audio.FadeInSec;
                video.AudioFadeOutSec = audio.FadeOutSec;
                if (audio.Keyframes.TryGetValue(AutomationKeys.Volume, out var volume))
                    video.Keyframes[AutomationKeys.Volume] = volume;
                foreach (var marker in audio.Markers)
                    if (!video.Markers.Any(m => Math.Abs(m.StartSec - marker.StartSec) < Eps && m.Name == marker.Name))
                        video.Markers.Add(marker);
                match.track.Clips.Remove(audio);
                if (match.track.Clips.Count == 0)
                    emptied.Add(match.track);
                merged++;
            }

            // a link group left with one clip is no group
            foreach (var group in timeline.Tracks.SelectMany(t => t.Clips)
                         .Where(c => !string.IsNullOrEmpty(c.LinkGroupId))
                         .GroupBy(c => c.LinkGroupId).Where(g => g.Count() == 1))
                group.Single().LinkGroupId = null;

            // audio tracks that only held a video's sound, now part of the video
            foreach (var track in emptied)
                timeline.Tracks.Remove(track);
            return merged;
        }

        /// <summary>
        /// True when the audio clip plays exactly the video clip's sound: the same file, place, part and speed,
        /// on, and with no effects of its own. Its fades become the sound's fades.
        /// </summary>
        private static bool SameSound(VideoClip video, AudioClip audio) =>
            audio.SourceId == video.SourceId
            && Math.Abs(audio.StartSec - video.StartSec) < Eps
            && Math.Abs(audio.DurSec - video.DurSec) < Eps
            && Math.Abs(audio.SrcInSec - video.SrcInSec) < Eps
            && Math.Abs(audio.SrcOutSec - video.SrcOutSec) < Eps
            && Math.Abs(audio.Speed - video.Speed) < Eps
            && audio.Enabled == video.Enabled
            && audio.Effects.Count == 0
            && !video.Keyframes.ContainsKey(AutomationKeys.Volume);
    }
}
