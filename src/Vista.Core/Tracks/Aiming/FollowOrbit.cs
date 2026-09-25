using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>Reads and sets a Follow Target offset as an orbit round its character.</summary>
public static class FollowOrbit
{
    private const float Epsilon = 1e-4f;

    /// <summary>The orbit of an offset in its character's frame.</summary>
    public static Orbit Of(ControlPoint offset)
    {
        var p = offset.Position;
        return new Orbit(CameraRotation.Sideways(p), Wrap(MathF.Atan2(p.X, p.Z)), p.Y);
    }

    /// <summary>The offset moved to <paramref name="orbit"/>: a change of angle swings it round the character and turns its yaw with it; aim, roll and FoV are otherwise kept.</summary>
    public static ControlPoint With(ControlPoint offset, Orbit orbit)
    {
        var current = Of(offset);
        var turned = new Anchor(Vector3.Zero, orbit.Angle - current.Angle).ToWorld(offset);
        var across = Vectors.FlatOr(turned.Position, -CameraRotation.Direction(orbit.Angle, 0f), Epsilon);
        var distance = MathF.Max(0f, orbit.Distance);
        return turned with { Position = new Vector3(across.X * distance, orbit.Height, across.Z * distance) };
    }

    private static float Wrap(float angle)
    {
        var wrapped = angle % MathF.Tau;
        return wrapped < 0f ? wrapped + MathF.Tau : wrapped;
    }
}
