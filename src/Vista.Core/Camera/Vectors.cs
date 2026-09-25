using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Angles between vectors, normalising with a fallback, and finiteness.</summary>
public static class Vectors
{
    /// <summary>The unsigned angle between two vectors of any length, in radians: precise when they're nearly the same or opposite.</summary>
    public static float AngleBetween(Vector3 a, Vector3 b) =>
        MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/> in radians, positive turning right-handed about unit <paramref name="axis"/>; both square to the axis.</summary>
    public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis) =>
        MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));

    /// <summary><paramref name="v"/> made unit length, or <paramref name="fallback"/> when it is no longer than <paramref name="shortest"/>.</summary>
    public static Vector3 NormalizeOr(Vector3 v, Vector3 fallback, float shortest = 0f) =>
        v.LengthSquared() > shortest * shortest ? Vector3.Normalize(v) : fallback;

    /// <summary><paramref name="v"/>'s level part made unit length, or <paramref name="fallback"/> when that is no longer than <paramref name="shortest"/>.</summary>
    public static Vector3 FlatOr(Vector3 v, Vector3 fallback, float shortest = 0f) =>
        NormalizeOr(v with { Y = 0f }, fallback, shortest);

    /// <summary>True when every component of <paramref name="v"/> is finite.</summary>
    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
