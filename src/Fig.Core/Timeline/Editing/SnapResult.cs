namespace Fig.Core.Timeline
{
    /// <summary>What a time snapped to.</summary>
    public enum SnapKind
    {
        /// <summary>Nothing nearby: just the nearest frame.</summary>
        Frame,
        /// <summary>A grid line of the timeline.</summary>
        Grid,
        /// <summary>The edge of a clip (or the start of the timeline).</summary>
        ClipEdge,
        /// <summary>A marker, or an end of a range marker.</summary>
        Marker,
        /// <summary>The playhead.</summary>
        Playhead,
    }

    /// <param name="Time">The snapped time, in seconds.</param>
    /// <param name="Kind">What it snapped to.</param>
    public readonly record struct SnapResult(double Time, SnapKind Kind)
    {
        /// <summary>True when it caught on something (a grid line, a clip edge, a marker, the playhead).</summary>
        public bool Caught => Kind != SnapKind.Frame;
    }
}
