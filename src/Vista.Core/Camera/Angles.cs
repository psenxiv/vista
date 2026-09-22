namespace Vista.Core.Camera;

/// <summary>Bringing an angle back into range, in the two conventions the camera uses.</summary>
public static class Angles
{
    /// <summary>The angle wrapped to within half a turn of zero.</summary>
    public static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);

    /// <summary>The shortest signed way round from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float Delta(float from, float to)
    {
        var delta = to - from;
        return delta - (MathF.Tau * MathF.Round(delta / MathF.Tau));
    }
}
