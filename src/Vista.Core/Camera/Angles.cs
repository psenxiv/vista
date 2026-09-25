namespace Vista.Core.Camera;

/// <summary>Degrees and radians, and bringing angles back into range in the two conventions the camera uses.</summary>
public static class Angles
{
    /// <summary>Radians in a degree.</summary>
    public const float Degree = MathF.PI / 180f;

    /// <summary>Radians in degrees.</summary>
    public static float Degrees(float radians) => radians / Degree;

    /// <summary>Degrees in radians.</summary>
    public static float Radians(float degrees) => degrees * Degree;

    /// <summary>The angle wrapped to within half a turn of zero.</summary>
    public static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);

    /// <summary>The shortest signed way round from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float Delta(float from, float to)
    {
        var delta = to - from;
        return delta - (MathF.Tau * MathF.Round(delta / MathF.Tau));
    }

    /// <summary>A sequence of angles (yaw or roll) with full turns added or taken away so consecutive values differ by at most half a turn.</summary>
    public static float[] Unwrap(IReadOnlyList<float> angles)
    {
        var result = new float[angles.Count];
        if (angles.Count == 0)
            return result;

        result[0] = angles[0];
        for (var i = 1; i < angles.Count; i++)
            result[i] = result[i - 1] + Delta(result[i - 1], angles[i]);

        return result;
    }
}
