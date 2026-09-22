using System.Numerics;

namespace CinematicCam.Core.Camera;

/// <summary>Derives the camera's up vector, which the game leaves rolled if we do not write it.</summary>
public static class CameraOrientation
{
    /// <summary>Reference direction used when the view is too close to vertical for world up.</summary>
    private static readonly Vector3 VerticalFallback = new(0f, 0f, -1f);

    /// <summary>
    /// The up vector for a view, matching the game's own formula: world up projected onto the
    /// plane perpendicular to the view direction, left un-normalised.
    /// </summary>
    public static Vector3 UpFor(Vector3 position, Vector3 lookAt)
    {
        var view = lookAt - position;
        if (view.LengthSquared() < float.Epsilon) return Vector3.UnitY;

        var forward = Vector3.Normalize(view);
        var reference = MathF.Abs(forward.Y) > 0.9999f ? VerticalFallback : Vector3.UnitY;

        return reference - (forward * Vector3.Dot(reference, forward));
    }

    /// <summary>The up vector for a view, rolled about the view direction by <paramref name="roll"/> radians.</summary>
    public static Vector3 UpFor(Vector3 position, Vector3 lookAt, float roll)
    {
        var up = UpFor(position, lookAt);
        var view = lookAt - position;
        if (roll == 0f || view.LengthSquared() < float.Epsilon) return up;

        return Vector3.Transform(up, Quaternion.CreateFromAxisAngle(Vector3.Normalize(view), roll));
    }
}
