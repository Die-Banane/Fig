using System;
using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>Beat markers shown on a clip before they are committed. Not part of the document.</summary>
    public sealed record BeatMarkerPreview(Clip Clip, IReadOnlyList<double> LocalTimes);

    public partial class TimelineEditor
    {
        /// <summary>
        /// Candidate beat markers being tuned (for example with a sensitivity slider). They live
        /// outside the document: no undo entry, no autosave, no <see cref="TimelineChanged"/> per tick.
        /// </summary>
        public BeatMarkerPreview? BeatPreview { get; private set; }

        public event Action? BeatPreviewChanged;

        /// <summary>Shows clip-local beat times on the clip. Replaces any earlier preview.</summary>
        public void SetBeatPreview(Clip clip, IReadOnlyList<double> localTimes)
        {
            BeatPreview = new BeatMarkerPreview(clip, GenerateMarkersFromBeats.CleanTimes(localTimes, clip.DurSec));
            BeatPreviewChanged?.Invoke();
        }

        public void ClearBeatPreview()
        {
            if (BeatPreview is null)
                return;
            BeatPreview = null;
            BeatPreviewChanged?.Invoke();
        }

        /// <summary>
        /// True while the clip's generated beat markers are about to be replaced by the preview,
        /// so views can hide them instead of drawing two sets.
        /// </summary>
        public bool IsSupersededByPreview(Clip clip, Marker marker)
            => BeatPreview is { } preview && ReferenceEquals(preview.Clip, clip) && marker.IsGeneratedBeat;

        /// <summary>Turns the preview into real markers as one undoable edit and clears it.</summary>
        public IReadOnlyList<Marker> CommitBeatPreview()
        {
            if (BeatPreview is not { } preview)
                return Array.Empty<Marker>();
            ClearBeatPreview();
            return AddBeatMarkers(preview.Clip, preview.LocalTimes);
        }
    }
}
