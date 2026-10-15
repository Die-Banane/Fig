namespace Fig.Core.Media;

/// <summary>
/// The expensive half of beat detection, kept so sensitivity and kick focus can be re-applied
/// instantly: decoding and spectral analysis run once, selecting markers from the result is cheap.
/// </summary>
public sealed class BeatAnalysis
{
    private readonly IReadOnlyList<double> _onsets;
    private readonly IReadOnlyList<double> _kicks;
    private readonly IReadOnlyList<double> _trackedBeats;
    private readonly double _framesPerSecond;
    private readonly double _offset;

    internal BeatAnalysis(IReadOnlyList<double> onsets, IReadOnlyList<double> kicks,
        IReadOnlyList<double> trackedBeats, double framesPerSecond, double offset)
    {
        _onsets = onsets;
        _kicks = kicks;
        _trackedBeats = trackedBeats;
        _framesPerSecond = framesPerSecond;
        _offset = offset;
    }

    public static BeatAnalysis Empty { get; } = new([], [], [], 1, 0);

    /// <summary>Beat or kick times in source seconds for a sensitivity between 0 (strong hits only) and 1 (more hits).</summary>
    public List<double> Select(double sensitivity, bool kicksOnly)
        => kicksOnly
            ? BeatAnalysisService.PickAttacks(_kicks, _framesPerSecond, _offset, sensitivity)
            : BeatAnalysisService.FilterBeatStrengths(_trackedBeats.ToList(), _onsets, _framesPerSecond, _offset, sensitivity);
}
