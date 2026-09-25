namespace Vista.Core.Display;

/// <summary>Round tick spacing for a graph axis.</summary>
public static class Ticks
{
    private static readonly float[] Factors = [1f, 2f, 5f, 10f];

    /// <summary>The number format for tick labels <paramref name="step"/> apart: as many decimals as the step has, up to two.</summary>
    public static string Format(float step) =>
        step >= 1f ? "0"
        : step >= 0.1f ? "0.0"
        : "0.00";

    /// <summary>The smallest of 1, 2 or 5 times a power of ten giving at most <paramref name="most"/> ticks over <paramref name="range"/>; 1 for an empty range.</summary>
    public static float Step(float range, int most)
    {
        if (!(range > 0f) || most < 1)
            return 1f;
        var raw = range / most;
        var magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
        foreach (var factor in Factors)
        {
            var step = factor * magnitude;
            if (step >= raw)
                return step;
        }

        return 10f * magnitude;
    }
}
