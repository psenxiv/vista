using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>Cumulative arc-length samples per segment, for walking a track by distance instead of by spline parameter.</summary>
public sealed class ArcLengthTable
{
    /// <summary>Chord samples taken across each segment when the table is built.</summary>
    public const int SamplesPerSegment = 60;

    private readonly float[][] _cumulative;

    /// <summary>Number of curve segments the table covers.</summary>
    public int SegmentCount { get; }

    /// <summary>Sum of every segment's arc length.</summary>
    public float TotalLength { get; }

    /// <summary>Samples the Catmull-Rom curve through <paramref name="points"/> once and builds the cumulative table.</summary>
    public ArcLengthTable(IReadOnlyList<Vector3> points)
    {
        SegmentCount = CatmullRom.SegmentCount(points.Count);
        _cumulative = new float[SegmentCount][];

        var total = 0f;
        for (var segment = 0; segment < SegmentCount; segment++)
        {
            var samples = new float[SamplesPerSegment + 1];
            var previous = CatmullRom.Evaluate(points, segment, 0f);
            for (var i = 1; i <= SamplesPerSegment; i++)
            {
                var current = CatmullRom.Evaluate(points, segment, (float)i / SamplesPerSegment);
                samples[i] = samples[i - 1] + Vector3.Distance(previous, current);
                previous = current;
            }

            _cumulative[segment] = samples;
            total += samples[^1];
        }

        TotalLength = total;
    }

    /// <summary>Arc length of one segment.</summary>
    public float SegmentLength(int segment) => _cumulative[CheckSegment(segment)][^1];

    /// <summary>Spline parameter t in [0, 1] whose arc distance from the segment start is fraction x SegmentLength.</summary>
    public float ParameterAt(int segment, float fraction)
    {
        var samples = _cumulative[CheckSegment(segment)];
        var length = samples[^1];
        if (length <= 0f) return fraction;

        var target = Math.Clamp(fraction, 0f, 1f) * length;

        var lo = 0;
        var hi = samples.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (samples[mid] <= target) lo = mid; else hi = mid;
        }

        var span = samples[hi] - samples[lo];
        var local = span <= 0f ? 0f : (target - samples[lo]) / span;
        return (lo + local) / SamplesPerSegment;
    }

    private int CheckSegment(int segment)
    {
        if (segment < 0 || segment >= SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(segment),
                SegmentCount == 0 ? "the table has no segments" : $"segment must be 0..{SegmentCount - 1}");
        return segment;
    }
}
