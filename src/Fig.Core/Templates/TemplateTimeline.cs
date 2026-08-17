using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Templates;

internal static class TemplateTimeline
{
    public static TimelineModel Clone(TimelineModel source, bool freshIds)
    {
        var trackIds = source.Tracks.ToDictionary(track => track.Id,
            track => freshIds ? Guid.NewGuid().ToString("N") : track.Id);
        var linkIds = source.Tracks.SelectMany(track => track.Clips)
            .Where(clip => !string.IsNullOrWhiteSpace(clip.LinkGroupId))
            .Select(clip => clip.LinkGroupId!)
            .Distinct()
            .ToDictionary(id => id, id => freshIds ? Guid.NewGuid().ToString("N") : id);

        var clone = new TimelineModel
        {
            Id = freshIds ? Guid.NewGuid().ToString("N") : source.Id,
            Rate = source.Rate,
            Revision = 0,
            IsAutosave = false,
            GlobalStartSec = source.GlobalStartSec,
            Markers = source.Markers.Select(marker => marker.Clone()).ToList(),
            Metadata = new Dictionary<string, System.Text.Json.JsonElement>(source.Metadata),
        };

        foreach (var track in source.Tracks)
        {
            var trackClone = new Track
            {
                Id = trackIds[track.Id],
                Kind = track.Kind,
                Index = track.Index,
                Name = track.Name,
                Visible = track.Visible,
                Muted = track.Muted,
                Markers = track.Markers.Select(marker => marker.Clone()).ToList(),
                Metadata = new Dictionary<string, System.Text.Json.JsonElement>(track.Metadata),
            };
            foreach (var clip in track.Clips)
            {
                var clipClone = ClipFactory.Clone(clip);
                clipClone.Id = freshIds ? Guid.NewGuid().ToString("N") : clip.Id;
                if (clip.LinkGroupId is { Length: > 0 } linkId)
                    clipClone.LinkGroupId = linkIds[linkId];
                trackClone.Clips.Add(clipClone);
            }
            clone.Tracks.Add(trackClone);
        }

        return clone;
    }
}
