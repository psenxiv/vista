using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Editing;

/// <summary>The ranges the editor's number fields clamp to, so invalid values never reach a track.</summary>
public static class EditLimits
{
    public const float MinLegSeconds = 0.1f;
    public const float MaxSeconds = 600f;

    /// <summary>A leg in seconds, 0.1 to 600; not a number becomes the minimum.</summary>
    public static float Leg(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, MinLegSeconds, MaxSeconds) : MinLegSeconds;

    /// <summary>A hold in seconds, 0 to 600; not a number becomes 0.</summary>
    public static float Hold(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, MaxSeconds) : 0f;

    /// <summary>A pitch in radians within the gizmo's limit; not a number becomes 0.</summary>
    public static float Pitch(float radians) => float.IsFinite(radians) ? Math.Clamp(radians, -TrackAim.PitchLimit, TrackAim.PitchLimit) : 0f;

    /// <summary>A yaw or roll in radians wrapped to within half a turn; not a number becomes 0.</summary>
    public static float Angle(float radians) => float.IsFinite(radians) ? MathF.IEEERemainder(radians, MathF.Tau) : 0f;

    /// <summary>A field of view in radians within the game's range; not a number becomes the minimum.</summary>
    public static float Fov(float radians, float min, float max) => float.IsFinite(radians) ? Math.Clamp(radians, min, max) : min;

    /// <summary>A position coordinate, or <paramref name="current"/> when the value is not finite.</summary>
    public static float Coordinate(float value, float current) => float.IsFinite(value) ? value : current;
}
