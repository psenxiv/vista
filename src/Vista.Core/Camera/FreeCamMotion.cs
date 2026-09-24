using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Free camera movement maths.</summary>
public static class FreeCamMotion
{
    /// <summary>Matches the range the game's own look-at target sits at, roughly 1.5 to 20.</summary>
    public const float LookAtDistance = 10f;

    /// <summary>Advances a camera position by one frame of input: along the camera's own facing and right, and straight up or down.</summary>
    /// <param name="input">(forward, up, right), each in [-1, 1].</param>
    /// <param name="speed">Units per second at full input.</param>
    public static Vector3 Step(Vector3 position, Vector3 input, Quaternion rotation, float speed, float deltaSeconds)
    {
        if (input == Vector3.Zero)
            return position;

        var move =
            (CameraRotation.Forward(rotation) * input.X)
            + (Vector3.UnitY * input.Y)
            + (Vector3.Transform(Vector3.UnitX, rotation) * input.Z);

        return position + (move * speed * deltaSeconds);
    }

    /// <summary>Turns a rotation by radians of yaw about the vertical, the other way while upside down so it follows the picture, then pitch about its own right, with no limit; signs follow the game's DirH and DirV.</summary>
    public static Quaternion Turn(Quaternion rotation, float yawDelta, float pitchDelta)
    {
        var vertical = CameraRotation.Up(rotation).Y < 0f ? -Vector3.UnitY : Vector3.UnitY;
        return Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(vertical, yawDelta)
                * rotation
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchDelta)
        );
    }

    /// <summary>Rolls a rotation about its own facing by <paramref name="angle"/> radians, positive rolling right.</summary>
    public static Quaternion Roll(Quaternion rotation, float angle) =>
        Quaternion.Normalize(rotation * Quaternion.CreateFromAxisAngle(new Vector3(0f, 0f, -1f), angle));

    /// <summary>A point ahead of the camera along its facing.</summary>
    public static Vector3 LookAtFrom(Vector3 position, float yaw, float pitch) =>
        position + (Direction(yaw, pitch) * LookAtDistance);

    /// <summary>A point ahead of the camera along the rotation's facing.</summary>
    public static Vector3 LookAtFrom(Vector3 position, Quaternion rotation) =>
        position + (CameraRotation.Forward(rotation) * LookAtDistance);

    /// <summary>Unit view direction. Sign convention measured in game, not assumed.</summary>
    private static Vector3 Direction(float yaw, float pitch)
    {
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cosPitch, MathF.Sin(pitch), -MathF.Cos(yaw) * cosPitch);
    }
}
