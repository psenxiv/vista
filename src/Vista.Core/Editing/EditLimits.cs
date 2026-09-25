using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Editing;

/// <summary>The ranges the pose fields keep to: a value that isn't finite changes nothing, and a finite one is clamped or wrapped.</summary>
public static class EditLimits
{
    /// <summary>Steepest pitch a point, the gizmo or the Point window can reach: straight up or down.</summary>
    public const float PitchLimit = MathF.PI / 2f;

    /// <summary>A pitch in radians, within ±90°, or <paramref name="current"/> when not finite.</summary>
    public static float Pitch(float radians, float current) =>
        float.IsFinite(radians) ? Math.Clamp(radians, -PitchLimit, PitchLimit) : current;

    /// <summary>A yaw or roll in radians wrapped to within half a turn, or <paramref name="current"/> when not finite.</summary>
    public static float Angle(float radians, float current) => float.IsFinite(radians) ? Angles.Wrap(radians) : current;

    public const float MinFov = 5f * MathF.PI / 180f;
    public const float MaxFov = 120f * MathF.PI / 180f;

    /// <summary>A field of view in radians, 5° to 120°, or <paramref name="current"/> when not finite.</summary>
    public static float Fov(float radians, float current) =>
        float.IsFinite(radians) ? Math.Clamp(radians, MinFov, MaxFov) : current;

    /// <summary><paramref name="position"/> with axis 0, 1 or 2 (X, Y or Z) set to <paramref name="value"/>, kept when it is not finite.</summary>
    public static Vector3 Coordinate(Vector3 position, int axis, float value) =>
        axis switch
        {
            0 => position with { X = Coordinate(value, position.X) },
            1 => position with { Y = Coordinate(value, position.Y) },
            2 => position with { Z = Coordinate(value, position.Z) },
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "An axis is 0, 1 or 2."),
        };

    /// <summary>A position coordinate, or <paramref name="current"/> when the value is not finite.</summary>
    private static float Coordinate(float value, float current) => float.IsFinite(value) ? value : current;
}
