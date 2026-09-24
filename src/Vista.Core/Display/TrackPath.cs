using System.Numerics;
using Vista.Core.Tracks.Spline;

namespace Vista.Core.Display;

/// <summary>Samples a track's path for drawing.</summary>
public static class TrackPath
{
    /// <summary>Caps the samples in one segment, so a very long leg cannot stall a frame.</summary>
    public const int MaxStepsPerSegment = 256;

    /// <summary>Points along the path through <paramref name="points"/>, about <paramref name="spacing"/> metres apart, starting and ending on the track.</summary>
    public static IReadOnlyList<Vector3> Sample(IReadOnlyList<Vector3> points, float spacing)
    {
        if (points.Count < 2) return points.ToArray();

        var table = new ArcLengthTable(points);
        var samples = new List<Vector3> { points[0] };
        for (var segment = 0; segment < table.SegmentCount; segment++)
        {
            var steps = Math.Clamp((int)MathF.Ceiling(table.SegmentLength(segment) / spacing), 1, MaxStepsPerSegment);
            for (var i = 1; i <= steps; i++)
                samples.Add(CatmullRom.Evaluate(points, segment, table.ParameterAt(segment, i / (float)steps)));
        }

        return samples;
    }
}
