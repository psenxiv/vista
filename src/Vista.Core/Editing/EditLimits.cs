using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Editing;

/// <summary>The ranges the editor's number fields clamp to, so invalid values never reach a track.</summary>
public static class EditLimits
{
    /// <summary>A leg in seconds, within the track's leg range; not a number becomes the minimum.</summary>
    public static float Leg(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, TrackEditing.MinLegSeconds, TrackEditing.MaxSeconds) : TrackEditing.MinLegSeconds;

    public const float MinShotSeconds = 0.2f;

    /// <summary>A track or leg speed in yalms per second, within the track's speed range; not a number becomes the default.</summary>
    public static float Speed(float speed) => float.IsFinite(speed) ? Math.Clamp(speed, TrackEditing.MinSpeed, TrackEditing.MaxSpeed) : TrackEditing.DefaultSpeed;

    /// <summary>A shot's duration in seconds, 0.2 to 3600; not a number becomes the minimum.</summary>
    public static float ShotDuration(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, MinShotSeconds, TrackEditing.MaxShotSeconds) : MinShotSeconds;

    /// <summary>A hold in seconds, 0 to the track's longest; not a number becomes 0.</summary>
    public static float Hold(float seconds) => float.IsFinite(seconds) ? Math.Clamp(seconds, 0f, TrackEditing.MaxSeconds) : 0f;

    /// <summary>A pitch in radians within the gizmo's limit; not a number becomes 0.</summary>
    public static float Pitch(float radians) => float.IsFinite(radians) ? Math.Clamp(radians, -TrackAim.PitchLimit, TrackAim.PitchLimit) : 0f;

    /// <summary>A yaw or roll in radians wrapped to within half a turn; not a number becomes 0.</summary>
    public static float Angle(float radians) => float.IsFinite(radians) ? Angles.Wrap(radians) : 0f;

    public const float MinFov = 5f * MathF.PI / 180f;
    public const float MaxFov = 120f * MathF.PI / 180f;

    /// <summary>A field of view in radians, 5° to 120°; not a number becomes the minimum.</summary>
    public static float Fov(float radians) => float.IsFinite(radians) ? Math.Clamp(radians, MinFov, MaxFov) : MinFov;

    /// <summary>A position coordinate, or <paramref name="current"/> when the value is not finite.</summary>
    public static float Coordinate(float value, float current) => float.IsFinite(value) ? value : current;
}
