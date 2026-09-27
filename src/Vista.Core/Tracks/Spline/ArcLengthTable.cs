using System.Numerics;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks.Spline;

/// <summary>Cumulative arc-length samples per segment, for walking a track by distance instead of by spline parameter, inverted by cubic Hermite.</summary>
public sealed class ArcLengthTable
{
    /// <summary>Chord samples taken across each segment when the table is built.</summary>
    public const int SamplesPerSegment = 60;

    private readonly float[][] _cumulative;
    private readonly float[][] _rates;

    /// <summary>Number of curve segments the table covers.</summary>
    public int SegmentCount { get; }

    /// <summary>Sum of every segment's arc length.</summary>
    public float TotalLength { get; }

    /// <summary>Samples the Catmull-Rom curve through <paramref name="points"/> once and builds the cumulative table.</summary>
    public ArcLengthTable(IReadOnlyList<Vector3> points)
    {
        SegmentCount = CatmullRom.SegmentCount(points.Count);
        _cumulative = new float[SegmentCount][];
        _rates = new float[SegmentCount][];

        var total = 0f;
        for (var segment = 0; segment < SegmentCount; segment++)
        {
            var samples = new float[SamplesPerSegment + 1];
            var rates = new float[SamplesPerSegment + 1];
            var previous = CatmullRom.Evaluate(points, segment, 0f);
            for (var i = 1; i <= SamplesPerSegment; i++)
            {
                var current = CatmullRom.Evaluate(points, segment, (float)i / SamplesPerSegment);
                samples[i] = samples[i - 1] + Vector3.Distance(previous, current);
                previous = current;
            }

            for (var i = 0; i <= SamplesPerSegment; i++)
                rates[i] = CatmullRom.Derivative(points, segment, (float)i / SamplesPerSegment).Length();

            _cumulative[segment] = samples;
            _rates[segment] = rates;
            total += samples[^1];
        }

        TotalLength = total;
    }

    /// <summary>Each segment's arc length, floored at <paramref name="minimum"/>.</summary>
    public float[] SegmentLengths(float minimum)
    {
        var lengths = new float[SegmentCount];
        for (var i = 0; i < lengths.Length; i++)
            lengths[i] = MathF.Max(SegmentLength(i), minimum);
        return lengths;
    }

    /// <summary>Arc length of one segment.</summary>
    public float SegmentLength(int segment) => _cumulative[CheckSegment(segment)][^1];

    /// <summary>Spline parameter t in [0, 1] whose arc distance from the segment start is fraction x SegmentLength, found by inverting the sampled distances with a monotone cubic Hermite.</summary>
    public float ParameterAt(int segment, float fraction)
    {
        var samples = _cumulative[CheckSegment(segment)];
        var length = samples[^1];
        if (length <= 0f)
            return fraction;

        var target = Fraction.Clamp(fraction) * length;

        var lo = Search.LastAtOrBelow(samples, target, 0, samples.Length - 1);
        var hi = lo + 1;

        var span = samples[hi] - samples[lo];
        if (span <= 0f)
            return lo / (float)SamplesPerSegment;
        var local = (target - samples[lo]) / span;
        var rates = _rates[segment];
        var slopes = (InverseSlope(rates[lo], span), InverseSlope(rates[hi], span));
        return (lo + Hermite.At(0f, 1f, slopes.Item1, slopes.Item2, local)) / SamplesPerSegment;
    }

    /// <summary>The inverse's slope at a sample in units of its interval: the true dt/ds from the path's speed <paramref name="rate"/> there, clamped to [0, 3] so the inverse stays monotone (Fritsch–Carlson); 3 where the path stands still.</summary>
    private static float InverseSlope(float rate, float span) =>
        rate <= 0f ? 3f : Math.Clamp(span * SamplesPerSegment / rate, 0f, 3f);

    private int CheckSegment(int segment)
    {
        if (segment < 0 || segment >= SegmentCount)
            throw new ArgumentOutOfRangeException(
                nameof(segment),
                SegmentCount == 0 ? "the table has no segments" : $"segment must be 0..{SegmentCount - 1}"
            );
        return segment;
    }
}
