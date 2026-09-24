using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Camera orientation as a rotation, so the camera can face any way: build one from angles or a facing, and read it back.</summary>
public static class CameraRotation
{
    /// <summary>How close two unit vectors' dot product must be to +-1 to treat them as parallel or opposite.</summary>
    private const float ParallelDot = 1f - 1e-6f;

    /// <summary>How close a vector's length must be to zero to treat it as degenerate.</summary>
    private const float DegenerateLength = 1e-6f;

    /// <summary>The rotation taking local forward (0,0,-1) to <paramref name="yaw"/>/<paramref name="pitch"/>'s facing, and local up (0,1,0) to that facing's upright up, rolled about the forward by <paramref name="roll"/>.</summary>
    /// <remarks>Composed from the three turns, so a quaternion's sign follows its angles: yaw π and −π give opposite signs, and a blend between unwrapped angles turns the way they do.</remarks>
    public static Quaternion FromAngles(float yaw, float pitch, float roll)
    {
        var yawTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var pitchTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        var rollTurn = Quaternion.CreateFromAxisAngle(new Vector3(0f, 0f, -1f), roll);
        return Quaternion.Concatenate(Quaternion.Concatenate(rollTurn, pitchTurn), yawTurn);
    }

    /// <summary>The inverse of <see cref="FromAngles"/>: pitch in [-pi/2, pi/2], yaw and roll in (-pi, pi].</summary>
    public static (float Yaw, float Pitch, float Roll) ToAngles(Quaternion rotation)
    {
        var forward = Forward(rotation);
        var up = Up(rotation);
        // Against the horizontal components, not asin(y): asin loses precision near straight up or down.
        var pitch = MathF.Atan2(forward.Y, MathF.Sqrt((forward.X * forward.X) + (forward.Z * forward.Z)));

        if (MathF.Abs(forward.Y) > ParallelDot)
        {
            var poleYaw = forward.Y > 0f ? MathF.Atan2(up.X, up.Z) : MathF.Atan2(-up.X, -up.Z);
            return (poleYaw, pitch, 0f);
        }

        var yaw = MathF.Atan2(-forward.X, -forward.Z);
        var upright = UprightUp(yaw, pitch);
        var roll = MathF.Atan2(Vector3.Dot(Vector3.Cross(upright, up), forward), Vector3.Dot(upright, up));
        return (yaw, pitch, roll);
    }

    /// <summary>The rotation's local forward, (0,0,-1) transformed into world space.</summary>
    public static Vector3 Forward(Quaternion rotation) => Vector3.Transform(new Vector3(0f, 0f, -1f), rotation);

    /// <summary>The rotation's local up, (0,1,0) transformed into world space.</summary>
    public static Vector3 Up(Quaternion rotation) => Vector3.Transform(Vector3.UnitY, rotation);

    /// <summary>The rotation taking local forward and up onto <paramref name="forward"/> (normalised) and <paramref name="up"/> squared to it. Falls back to <see cref="Upright"/> when <paramref name="up"/> is parallel to <paramref name="forward"/>.</summary>
    public static Quaternion FromBasis(Vector3 forward, Vector3 up)
    {
        var f = Vector3.Normalize(forward);
        var squared = up - (f * Vector3.Dot(f, up));
        var length = squared.Length();
        var u = length < DegenerateLength ? Upright(f) : squared / length;
        return FromForwardAndUp(f, u);
    }

    /// <summary>The unit upright up for a facing: world up projected off the forward, normalised. Falls back to (0,0,1) for a facing straight up and (0,0,-1) for straight down, matching <see cref="FromAngles"/>'s poles.</summary>
    public static Vector3 Upright(Vector3 forward)
    {
        var f = Vector3.Normalize(forward);
        var squared = Vector3.UnitY - (f * f.Y);
        var length = squared.Length();
        return length < DegenerateLength ? new Vector3(0f, 0f, f.Y > 0f ? 1f : -1f) : squared / length;
    }

    /// <summary>The unsigned angle, in radians, the picture turns about its own centre between two frames: <paramref name="upA"/> carried square to <paramref name="forwardB"/> by the minimal rotation from <paramref name="forwardA"/>, against <paramref name="upB"/>.</summary>
    public static float Twist(Vector3 forwardA, Vector3 upA, Vector3 forwardB, Vector3 upB)
    {
        var carried = Vector3.Transform(upA, MinimalRotation(forwardA, forwardB));
        var dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(carried), Vector3.Normalize(upB)), -1f, 1f);
        return MathF.Acos(dot);
    }

    /// <summary>The unrolled upright up at yaw and pitch: the pitch-derivative of the facing, defined at the poles too.</summary>
    private static Vector3 UprightUp(float yaw, float pitch)
    {
        var sinPitch = MathF.Sin(pitch);
        return new Vector3(MathF.Sin(yaw) * sinPitch, MathF.Cos(pitch), MathF.Cos(yaw) * sinPitch);
    }

    /// <summary>Builds the rotation whose local forward and up land on the given, already orthonormal, forward and up.</summary>
    private static Quaternion FromForwardAndUp(Vector3 forward, Vector3 up)
    {
        var right = Vector3.Cross(forward, up);
        var matrix = new Matrix4x4(
            right.X,
            right.Y,
            right.Z,
            0f,
            up.X,
            up.Y,
            up.Z,
            0f,
            -forward.X,
            -forward.Y,
            -forward.Z,
            0f,
            0f,
            0f,
            0f,
            1f
        );
        return Quaternion.CreateFromRotationMatrix(matrix);
    }

    /// <summary>The shortest rotation taking unit vector <paramref name="from"/> onto unit vector <paramref name="to"/>.</summary>
    public static Quaternion MinimalRotation(Vector3 from, Vector3 to)
    {
        var a = Vector3.Normalize(from);
        var b = Vector3.Normalize(to);
        var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        if (dot > ParallelDot)
            return Quaternion.Identity;

        if (dot < -ParallelDot)
        {
            var arbitrary = MathF.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            var axis180 = Vector3.Normalize(Vector3.Cross(a, arbitrary));
            return Quaternion.CreateFromAxisAngle(axis180, MathF.PI);
        }

        var axis = Vector3.Normalize(Vector3.Cross(a, b));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }
}
