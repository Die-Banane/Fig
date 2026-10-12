using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// An editorial annotation pinned to a point (or short span) of a clip, track, or
    /// timeline. <see cref="StartSec"/> is relative to the object it sits on: for a clip
    /// marker it is an offset from the clip start; for track and timeline markers it is
    /// an absolute position on the timeline.
    /// </summary>
    public sealed class Marker
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public double StartSec { get; set; }
        public double DurSec { get; set; }
        public string Color { get; set; } = "#ffd60a";

        /// <summary>A range marker spans time (it has a duration); a point marker (bookmark) does not.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsRange => DurSec > 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public double EndSec => StartSec + DurSec;

        /// <summary>For range markers: whether "Export marked ranges" offers this range.</summary>
        public bool IncludeInExport { get; set; } = true;

        /// <summary>For range markers: the output file name to use when exporting (null = derived from the name).</summary>
        public string? OutputName { get; set; }

        /// <summary>Optional source-format provenance (e.g. cmx_3600 reel / color).</summary>
        public Dictionary<string, JsonElement>? Metadata { get; set; }

        public Marker Clone()
        {
            return new Marker
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
                StartSec = StartSec,
                DurSec = DurSec,
                Color = Color,
                IncludeInExport = IncludeInExport,
                OutputName = OutputName,
                Metadata = Metadata is null ? null : new Dictionary<string, JsonElement>(Metadata),
            };
        }
    }
}
