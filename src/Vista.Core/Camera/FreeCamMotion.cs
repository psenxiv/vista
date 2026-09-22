using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Free camera movement maths.</summary>
public static class FreeCamMotion
{
    /// <summary>Matches the range the game's own look-at target sits at, roughly 1.5 to 20.</summary>
    private const float LookAtDistance = 10f;

    /// <summary>Advances a camera position by one frame of input.</summary>
    /// <param name="input">(forward, up, right), each in [-1, 1].</param>
    /// <param name="yaw">Horizontal angle in radians, as the game reports DirH.</param>
    /// <param name="pitch">Vertical angle in radians. Positive looks up.</param>
    /// <param name="speed">Units per second at full input.</param>
    public static Vector3 Step(Vector3 position, Vector3 input,
                               float yaw, float pitch, float speed, float deltaSeconds)
    {
        if (input == Vector3.Zero) return position;

        var move = (Direction(yaw, pitch) * input.X)
                 + (Vector3.UnitY * input.Y)
                 + (Right(yaw) * input.Z);

        return position + (move * speed * deltaSeconds);
    }

    /// <summary>Turns a mouse-look change in yaw and pitch so it follows the screen when the camera is rolled by <paramref name="roll"/> radians.</summary>
    public static (float Yaw, float Pitch) RollLook(float yawDelta, float pitchDelta, float roll)
    {
        var cos = MathF.Cos(roll);
        var sin = MathF.Sin(roll);
        return ((yawDelta * cos) - (pitchDelta * sin), (yawDelta * sin) + (pitchDelta * cos));
    }

    /// <summary>A point ahead of the camera along its facing.</summary>
    public static Vector3 LookAtFrom(Vector3 position, float yaw, float pitch)
        => position + (Direction(yaw, pitch) * LookAtDistance);

    /// <summary>Unit view direction. Sign convention measured in game, not assumed.</summary>
    private static Vector3 Direction(float yaw, float pitch)
    {
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cosPitch, MathF.Sin(pitch), -MathF.Cos(yaw) * cosPitch);
    }

    /// <summary>Horizontal strafe axis, so rising never drifts sideways.</summary>
    private static Vector3 Right(float yaw)
        => new(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
}
