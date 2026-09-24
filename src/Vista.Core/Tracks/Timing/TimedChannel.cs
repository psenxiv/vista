namespace Vista.Core.Tracks.Timing;

/// <summary>A value blended over time through each point's value, held while the point holds, with a slope at each point that carries the rate straight through it.</summary>
public sealed class TimedChannel
{
    /// <summary>Below this, in seconds, a leg or hold counts as taking no time.</summary>
    public const float MinSeconds = 1e-4f;

    private readonly float[] values;
    private readonly float[] arrive;
    private readonly float[] depart;
    private readonly float[] slopes;

    /// <summary>A channel through <paramref name="values"/>, each reached at <paramref name="arrive"/> and left at <paramref name="depart"/>, in seconds.</summary>
    public TimedChannel(IReadOnlyList<float> values, IReadOnlyList<float> arrive, IReadOnlyList<float> depart)
    {
        if (values.Count == 0 || arrive.Count != values.Count || depart.Count != values.Count)
            throw new ArgumentException("A channel needs a value, an arrival and a departure for every point.");
        this.values = [.. values];
        this.arrive = [.. arrive];
        this.depart = [.. depart];
        slopes = Enumerable.Range(0, values.Count).Select(Slope).ToArray();
    }

    /// <summary>The value at <paramref name="time"/> seconds, held before the first point and after the last.</summary>
    public float At(double time)
    {
        var n = values.Length;
        if (n == 1) return values[0];
        if (time >= arrive[n - 1]) return values[n - 1];

        for (var leg = 1; leg < n; leg++)
        {
            if (time > arrive[leg]) continue;
            var start = depart[leg - 1];
            if (time <= start) return values[leg - 1];
            var span = arrive[leg] - start;
            var u = (float)((time - start) / span);
            return Hermite.At(values[leg - 1], values[leg], slopes[leg - 1] * span, slopes[leg] * span, u);
        }

        return values[n - 1];
    }

    /// <summary>Point <paramref name="i"/>'s slope per second: 0 through a hold, half the one-sided slope at an end, and the time-weighted Catmull-Rom slope between.</summary>
    private float Slope(int i)
    {
        var n = values.Length;
        if (n == 1 || depart[i] - arrive[i] > MinSeconds) return 0f;
        if (i == 0) return OneSided(1) / 2f;
        if (i == n - 1) return OneSided(n - 1) / 2f;

        var before = arrive[i] - depart[i - 1];
        var after = arrive[i + 1] - depart[i];
        if (before <= MinSeconds || after <= MinSeconds) return 0f;
        return (((values[i] - values[i - 1]) / before * after) + ((values[i + 1] - values[i]) / after * before)) / (before + after);
    }

    /// <summary>Leg <paramref name="leg"/>'s average slope per second, or 0 when it takes no time.</summary>
    private float OneSided(int leg)
    {
        var span = arrive[leg] - depart[leg - 1];
        return span <= MinSeconds ? 0f : (values[leg] - values[leg - 1]) / span;
    }
}
