using System.Numerics;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A Follow Target offset seen from the character: distance along the ground, angle round them (radians, 0 behind, a quarter turn to their right) and height above their feet.</summary>
public readonly record struct Orbit(float Distance, float Angle, float Height);

/// <summary>Reads and sets a Follow Target offset as an orbit round its character.</summary>
public static class FollowOrbit
{
    private const float Epsilon = 1e-4f;

    /// <summary>The orbit of an offset in its character's frame.</summary>
    public static Orbit Of(ControlPoint offset)
    {
        var p = offset.Position;
        return new Orbit(MathF.Sqrt((p.X * p.X) + (p.Z * p.Z)), Wrap(MathF.Atan2(p.X, p.Z)), p.Y);
    }

    /// <summary>The offset moved to <paramref name="orbit"/>: a change of angle swings it round the character and turns its yaw with it; aim, roll and FoV are otherwise kept.</summary>
    public static ControlPoint With(ControlPoint offset, Orbit orbit)
    {
        var current = Of(offset);
        var turned = new Anchor(Vector3.Zero, orbit.Angle - current.Angle).ToWorld(offset);
        var across = current.Distance > Epsilon
            ? Vector3.Normalize(turned.Position with { Y = 0f })
            : new Vector3(MathF.Sin(orbit.Angle), 0f, MathF.Cos(orbit.Angle));
        var distance = MathF.Max(0f, orbit.Distance);
        return turned with { Position = new Vector3(across.X * distance, orbit.Height, across.Z * distance) };
    }

    private static float Wrap(float angle)
    {
        var wrapped = angle % MathF.Tau;
        return wrapped < 0f ? wrapped + MathF.Tau : wrapped;
    }
}
