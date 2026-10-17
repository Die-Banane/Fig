using System;
using System.Linq;

namespace Fig.Core.Project
{
    /// <summary>
    /// Upgrades a project saved when a clip whose file had another frame rate than the timeline was retimed to it:
    /// it played at <c>Speed × SourceRate / Rate</c>. Clips now play in real time, so that factor moves into
    /// <see cref="Fig.Core.Timeline.Clip.Speed"/> and every clip keeps its place, length and sound.
    /// </summary>
    public static class RealTimeMediaMigration
    {
        public const int Version = 2;

        /// <summary>Upgrades the project if it is older than <see cref="Version"/>. Returns how many clips changed speed.</summary>
        public static int Migrate(Project project)
        {
            if (project.FormatVersion >= Version)
                return 0;
            var changed = 0;
            foreach (var timeline in project.Timelines)
            {
                var fps = timeline.Rate.Fps;
                if (!double.IsFinite(fps) || fps <= 0)
                    continue;
                foreach (var clip in timeline.Tracks.SelectMany(t => t.Clips))
                {
                    if (clip.SourceRate is not { Den: > 0 } source || source.Fps <= 0)
                        continue;
                    var ratio = source.Fps / fps;
                    if (Math.Abs(ratio - 1) < 1e-6)
                        continue;
                    clip.Speed = (clip.Speed <= 0 ? 1 : clip.Speed) * ratio;
                    changed++;
                }
            }
            project.FormatVersion = Version;
            return changed;
        }
    }
}
