using System;
using System.IO;
using FFmpeg.AutoGen;
using NAudio.Dsp;

namespace Fig.Core.Media;



public partial class BeatAnalysisService
{


    private readonly IMediaService _media;

    public BeatAnalysisService(IMediaService media)
    {
        _media = media;
    }

    static double HztoMel(double hz) => 2595 * Math.Log10(1 + hz / 700);
    static double MelToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

    private static double[,] CreateMelFilters(int sampleRate, int windowSize, int bandCount)
    {
        int binCount = windowSize / 2 + 1;
        double maxMel = HztoMel(sampleRate / 2.0);
        double[] bandHz = new double[bandCount + 2];

        for (int i = 0; i < bandHz.Length; i++)
        {
            double mel = maxMel * i / (bandHz.Length - 1);
            bandHz[i] = MelToHz(mel);
        }

        double[,] filters = new double[bandCount, binCount];
        for (int band = 0; band < bandCount; band++)
        {
            double left = bandHz[band];
            double center = bandHz[band + 1];
            double right = bandHz[band + 2];

            for (int bin = 0; bin < binCount; bin++)
            {
                double hz = (double)bin * sampleRate / windowSize;
                // The two slopes meet at a weight of one at the center.
                double rising = (hz - left) / (center - left);
                double falling = (right - hz) / (right - center);
                filters[band, bin] = Math.Max(0, Math.Min(rising, falling));
            }
        }

        return filters;
    }

    /// <summary>
    /// Decodes and analyzes a source range once. The result answers any sensitivity cheaply, which
    /// is what lets the UI move a slider and see markers follow in real time.
    /// </summary>
    public BeatAnalysis Analyze(string sourcePath, double startSec = 0, double? durationSec = null)
    {
        /*
        Index:   0   1   2   3   4   5
        Sample: L0  R0  L1  R1  L2  R2
        */
        int hopSize = 256;
        int sampleRate = 22050;


        double duration = durationSec ?? (_media.Probe(sourcePath).DurationSec - startSec);
        if (!double.IsFinite(startSec) || startSec < 0 || !double.IsFinite(duration) || duration <= 0)
            return BeatAnalysis.Empty;
        float[] stereo = _media.DecodeSamples(sourcePath, startSec, duration, sampleRate);
        float[] mono = new float[stereo.Length / 2]; // one channel

        for (int i = 0; i < mono.Length; i++)
        {
            // Average each left/right pair once before analyzing windows.
            mono[i] = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5f;
        }

        int windowSize = 1024; // samples
        float[] window = new float[windowSize]; // sample window recreation
        Complex[] spectrum = new Complex[windowSize];
        int bandCount = 40;
        double[,] melFilters = CreateMelFilters(sampleRate, windowSize, bandCount);
        double[] logBands = new double[bandCount];
        double[] previousBands = new double[bandCount];
        List<double> onsetStrengths = [];
        List<double> kickStrengths = [];
        float[] previousMagnitudes = new float[windowSize / 2 + 1];

        for (int start = 0; start + windowSize <= mono.Length; start += hopSize)
        {
            for (int i = 0; i < windowSize; i++)
            {
                double weight = 0.5 - (0.5 * Math.Cos((2 * Math.PI * i) / windowSize));
                window[i] = mono[start + i] * (float)weight;
            }



            for (int i = 0; i < windowSize; i++)
            {
                spectrum[i].X = window[i];
                spectrum[i].Y = 0;
            }
            FastFourierTransform.FFT(true, 10, spectrum);

            float[] magnitudes = new float[windowSize / 2 + 1];

            for (int i = 0; i < magnitudes.Length; i++)
            {
                float real = spectrum[i].X;
                float imaginary = spectrum[i].Y;

                magnitudes[i] = MathF.Sqrt(real * real + imaginary * imaginary);
            }

            for (int band = 0; band < bandCount; band++)
            {
                double magnitude = 0;
                for (int bin = 0; bin < magnitudes.Length; bin++)
                {
                    magnitude += magnitudes[bin] * melFilters[band, bin];
                }

                // Floor the magnitude so silent bands never produce log(0).
                logBands[band] = 20 * Math.Log10(Math.Max(magnitude, 1e-8));
            }

            // Positive spectral changes in the bass range favor kick-like attacks.
            // These are low-frequency transients, not an instrument classifier.
            double kick = 0;
            for (int bin = 0; bin < magnitudes.Length; bin++)
            {
                double hz = (double)bin * sampleRate / windowSize;
                if (hz >= 35 && hz <= 180)
                    kick += Math.Max(0, magnitudes[bin] - previousMagnitudes[bin]);
                previousMagnitudes[bin] = magnitudes[bin];
            }
            kickStrengths.Add(start == 0 ? 0 : kick);

            double onset = 0;

            for (int band = 0; band < bandCount; band++)
            {
                double increase = logBands[band] - previousBands[band];
                onset += Math.Max(0, increase);

                previousBands[band] = logBands[band];
            }

            onsetStrengths.Add(start == 0 ? 0 : onset);
        }


        double framesPerSecond = (double)sampleRate / hopSize;
        double offset = startSec + windowSize / (2.0 * sampleRate);
        var beats = TrackBeats(onsetStrengths, framesPerSecond, offset);
        return new BeatAnalysis(onsetStrengths, kickStrengths, beats, framesPerSecond, offset);
    }

    internal static List<double> FilterBeatStrengths(List<double> beats, IReadOnlyList<double> onsets,
        double framesPerSecond, double offset, double sensitivity)
    {
        if (onsets.Count == 0)
            return [];
        sensitivity = double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0, 1) : 0.5;
        double threshold = onsets.Max() * 0.6 * (1 - sensitivity);
        return beats.Where(time =>
        {
            int frame = Math.Clamp((int)Math.Round((time - offset) * framesPerSecond), 0, onsets.Count - 1);
            double strength = 0;
            for (int i = Math.Max(0, frame - 2); i <= Math.Min(onsets.Count - 1, frame + 2); i++)
                strength = Math.Max(strength, onsets[i]);
            return strength >= threshold;
        }).ToList();
    }

    internal static List<double> PickAttacks(IReadOnlyList<double> strengths, double framesPerSecond,
        double offset, double sensitivity)
    {
        if (strengths.Count < 3 || strengths.Max() <= 1e-8)
            return [];
        sensitivity = double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0, 1) : 0.5;
        double threshold = strengths.Max() * (0.6 - 0.55 * sensitivity);
        int radius = Math.Max(1, (int)Math.Round(framesPerSecond * 0.25));
        List<int> candidates = [];
        for (int i = 1; i < strengths.Count - 1; i++)
        {
            if (strengths[i] < threshold || strengths[i] <= strengths[i - 1] || strengths[i] < strengths[i + 1])
                continue;
            var neighborhood = new List<double>();
            for (int j = Math.Max(0, i - radius); j <= Math.Min(strengths.Count - 1, i + radius); j++)
                neighborhood.Add(strengths[j]);
            neighborhood.Sort();
            if (strengths[i] > neighborhood[neighborhood.Count / 2] * 1.5)
                candidates.Add(i);
        }
        // Strongest first prevents multiple markers around the same attack.
        int spacing = Math.Max(1, (int)Math.Round(framesPerSecond * 0.12));
        List<int> selected = [];
        foreach (int frame in candidates.OrderByDescending(i => strengths[i]).ThenBy(i => i))
            if (selected.All(other => Math.Abs(other - frame) >= spacing))
                selected.Add(frame);
        return selected.OrderBy(i => i).Select(i => offset + i / framesPerSecond).ToList();
    }

    // Separated from decoding so the tracker can be checked with known rhythmic inputs.
    internal static List<double> TrackBeats(IReadOnlyList<double> onsets, double framesPerSecond, double timeOffset = 0)
    {
        if (onsets.Count < 2 || onsets.Max() <= 1e-8)
            return [];

        double mean = onsets.Average();
        double deviation = Math.Sqrt(onsets.Sum(value => (value - mean) * (value - mean)) / onsets.Count);
        if (deviation <= 1e-8)
            return [];
        double[] strength = onsets.Select(value => (value - mean) / deviation).ToArray();

        int minLag = (int)Math.Ceiling(framesPerSecond * 60 / 180);
        int maxLag = Math.Min((int)Math.Floor(framesPerSecond * 60 / 60), strength.Length / 2);

        double bestScore = 0;
        int bestLag = 0;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double score = 0;

            for (int i = lag; i < strength.Length; i++)
            {
                score += strength[i] * strength[i - lag];
            }

            score /= strength.Length - lag;
            // A broad preference for 120 BPM helps resolve half/double-tempo ambiguity.
            double octaves = Math.Log2((60 * framesPerSecond / lag) / 120);
            score *= Math.Exp(-0.5 * octaves * octaves);

            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        if (bestLag == 0)
            return [];

        // Ellis-style dynamic programming: reward attacks and penalize irregular spacing.
        double[] scores = new double[strength.Length];
        int[] previous = Enumerable.Repeat(-1, strength.Length).ToArray();
        int firstInterval = Math.Max(1, bestLag / 2);
        int lastInterval = bestLag * 2;
        for (int i = 0; i < strength.Length; i++)
        {
            double bestPrevious = 0;
            for (int interval = firstInterval; interval <= lastInterval && interval <= i; interval++)
            {
                int predecessor = i - interval;
                double error = Math.Log((double)interval / bestLag);
                double candidate = scores[predecessor] - 100 * error * error;
                if (candidate > bestPrevious)
                {
                    bestPrevious = candidate;
                    previous[i] = predecessor;
                }
            }
            scores[i] = strength[i] + bestPrevious;
        }

        int end = -1;
        for (int i = 0; i < scores.Length; i++)
            if (strength[i] > 0 && (end < 0 || scores[i] >= scores[end]))
                end = i;

        List<double> beats = [];
        for (int i = end; i >= 0; i = previous[i])
            beats.Add(timeOffset + i / framesPerSecond);
        beats.Reverse();
        return beats;

    }
}
