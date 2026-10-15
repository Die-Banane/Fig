namespace Fig.Core.Timeline;

public partial class TimelineEditor
{
    /// <summary>
    /// Splits every video spanning a selected marker's frame-aligned timeline position.
    /// Linked video/audio follows; unrelated audio and text are left alone. A range marker
    /// supplies its start position. All cuts form one history entry.
    /// </summary>
    public int SplitVideoAtSelectedMarkers()
    {
        var times = Selection.SelectedMarkerIds.Select(FindMarker)
            .Where(location => location is not null)
            .Select(location => (location!.Clip?.StartSec ?? 0) + location.Marker.StartSec)
            .Where(double.IsFinite).Select(SnapTime).Distinct().OrderByDescending(time => time).ToArray();
        var videos = Document.Tracks.SelectMany(track => track.Clips).OfType<VideoClip>().ToArray();
        var commands = new List<IEditCommand>();
        int videoCuts = 0;

        // Descending cuts keep the original clip IDs valid for every planned cut: each
        // earlier point remains in the original left half rather than a newly created clip.
        foreach (double time in times)
        {
            var seen = new HashSet<string>();
            foreach (var video in videos.Where(clip => Inside(clip, time)))
            {
                if (seen.Contains(video.Id)) continue;
                var group = LinkGroup(video.Id)
                    .Where(clip => (clip is VideoClip or AudioClip) && Inside(clip, time)).ToArray();
                string? rightGroup = string.IsNullOrEmpty(video.LinkGroupId) ? null : Guid.NewGuid().ToString("N");
                foreach (var clip in group)
                {
                    if (!seen.Add(clip.Id)) continue;
                    commands.Add(new CutCommand(this, clip.Id, time, rightGroup));
                    if (clip is VideoClip) videoCuts++;
                }
            }
        }

        if (commands.Count == 0) return 0;
        History.Execute(new CompositeCommand(commands.ToArray()));
        RaiseChanged();
        return videoCuts;
    }

    private static bool Inside(Clip clip, double time)
        => time > clip.StartSec + 1e-8 && time < clip.StartSec + clip.DurSec - 1e-8;
}
