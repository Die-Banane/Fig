using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>True when any of the clips is a video clip still playing its own sound.</summary>
        public bool CanDetachAudio(IEnumerable<string> clipIds)
            => clipIds.Any(id => FindClip(id) is VideoClip { PlaysAudio: true });

        /// <summary>
        /// Moves each video clip's sound to an audio clip of its own, on the first audio track with room (or a new
        /// one): the same part of the file, place, speed, level, sound fades and volume keyframes, so it sounds the same.
        /// The video clip stops playing it. The new clips are not linked, so they move and trim on their own.
        /// One undo step; returns the new audio clips.
        /// </summary>
        public IReadOnlyList<AudioClip> DetachAudio(IEnumerable<string> clipIds)
        {
            var videos = clipIds.Distinct().Select(FindClip).OfType<VideoClip>().Where(v => v.PlaysAudio).ToList();
            var created = new List<AudioClip>();
            if (videos.Count == 0)
                return created;

            using (History.Group())
            {
                foreach (var video in videos)
                {
                    var audio = new AudioClip
                    {
                        SourceId = video.SourceId,
                        SourceRate = video.SourceRate,
                        StartSec = video.StartSec,
                        DurSec = video.DurSec,
                        SrcInSec = video.SrcInSec,
                        SrcOutSec = video.SrcOutSec,
                        Speed = video.Speed,
                        Volume = video.Volume,
                        FadeInSec = video.AudioFadeInSec,
                        FadeOutSec = video.AudioFadeOutSec,
                        Enabled = video.Enabled,
                    };
                    if (video.Keyframes.TryGetValue(AutomationKeys.Volume, out var volume))
                        audio.Keyframes[AutomationKeys.Volume] = new List<KeyframePoint>(volume);

                    History.Execute(new SetClipValueCommand<bool>(this, [video.Id], "detach audio",
                        c => ((VideoClip)c).PlaysAudio, (c, v) => ((VideoClip)c).PlaysAudio = v, false));
                    var track = Document.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio
                        && !WouldOverlap(t.Id, audio.StartSec, audio.DurSec)) ?? AddTrack(TrackKind.Audio);
                    History.Execute(new AddClipCommand(this, track.Id, audio));
                    created.Add(audio);
                }
            }
            RefreshTrackIndices();
            RaiseChanged();
            return created;
        }
    }
}
