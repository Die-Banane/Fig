using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fig.Core.Media;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Project
{
    public class Project
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string AppVersion { get; set; } = "1.0";

        /// <summary>
        /// What shape the saved timeline has, so a project from an earlier version is upgraded when it opens.
        /// 0: a video's sound is a linked audio clip. 1: it is part of the video clip (<see cref="EmbeddedAudioMigration"/>).
        /// 2: clips play in real time, not retimed to the timeline's frame rate (<see cref="RealTimeMediaMigration"/>).
        /// A file without the field is 0.
        /// </summary>
        public int FormatVersion { get; set; }

        public const int CurrentFormatVersion = RealTimeMediaMigration.Version;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>Absolute path to the project card thumbnail (first composited timeline frame).</summary>
        public string? Thumbnail { get; set; }

        public List<MediaAsset> Media { get; set; } = new();
        public List<TimelineModel> Timelines { get; set; } = new();
        public ExportSettings Export { get; set; } = new();

        /// <summary>Effects the user built from nodes. Stored with the project so it opens anywhere.</summary>
        public List<Fig.Core.Timeline.EffectGraph> EffectGraphs { get; set; } = new();

        /// <summary>Source-format provenance preserved across imports.</summary>
        public Dictionary<string, JsonElement> Metadata { get; set; } = new();

        public static Project Create(string name) => new() { Name = name, FormatVersion = CurrentFormatVersion };

        public bool RelinkMedia(string oldPath, string newPath)
        {
            var asset = Media.FirstOrDefault(m =>
                string.Equals(m.Url, oldPath, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
                return false;

            asset.Url = newPath;
            asset.Offline = false;
            return true;
        }
    }
}
