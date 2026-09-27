using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A value blended by distance along the path through each point's value, with a slope at each point that carries the rate straight through it, limited by <see cref="Hermite.KeySlope(IReadOnlyList{float}, IReadOnlyList{float}, int)"/>.</summary>
public sealed class PathChannel
{
    private readonly float[] values;
    private readonly float[] distances;
    private readonly float[] slopes;

    /// <summary>A channel through <paramref name="values"/>, each at <paramref name="distances"/> along the path, which strictly increase.</summary>
    public PathChannel(IReadOnlyList<float> values, IReadOnlyList<float> distances)
    {
        Validate(values.Count, distances);
        this.values = [.. values];
        this.distances = [.. distances];
        var changes = Enumerable.Range(0, values.Count).Select(i => i == 0 ? 0f : values[i] - values[i - 1]).ToArray();
        slopes = Enumerable
            .Range(0, values.Count)
            .Select(i => values.Count == 1 ? 0f : Hermite.KeySlope(distances, changes, i))
            .ToArray();
    }

    /// <summary>The value at <paramref name="distance"/> along the path, held before the first point and after the last.</summary>
    public float At(float distance)
    {
        var n = values.Length;
        if (n == 1 || distance <= distances[0])
            return values[0];
        if (distance >= distances[n - 1])
            return values[n - 1];

        var leg = Search.LastAtOrBelow(distances, distance, 0, n - 1) + 1;
        var span = distances[leg] - distances[leg - 1];
        var u = (distance - distances[leg - 1]) / span;
        return Hermite.At(values[leg - 1], values[leg], slopes[leg - 1] * span, slopes[leg] * span, u);
    }

    /// <summary>Refuses anything but a distance for each of <paramref name="count"/> points, strictly increasing.</summary>
    internal static void Validate(int count, IReadOnlyList<float> distances)
    {
        if (count == 0 || distances.Count != count)
            throw new ArgumentException("A channel needs a distance for every point.");
        for (var i = 1; i < count; i++)
        {
            if (!(distances[i] > distances[i - 1]))
                throw new ArgumentException("A channel's distances must strictly increase.");
        }
    }
}
