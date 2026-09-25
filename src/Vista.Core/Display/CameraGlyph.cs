using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Display;

/// <summary>A wireframe camera in world space: a pyramid from the apex opening along the aim, with an up tab on its top edge.</summary>
/// <param name="Corners">The face's corners in outline order, the first two along the top edge.</param>
public sealed record CameraGlyph(Vector3 Apex, Vector3[] Corners, Vector3 TabLeft, Vector3 TabTip, Vector3 TabRight)
{
    /// <summary>Widest half-angle drawn, so an extreme FoV stays finite.</summary>
    private const float MaxHalfAngle = 80f * MathF.PI / 180f;

    /// <summary>Half of the game's default 44.7° FoV, used when the FoV is not a number.</summary>
    private const float DefaultHalfAngle = 0.39f;

    private const float TabHalfWidth = 0.35f;
    private const float TabHeight = 0.5f;

    /// <summary>Point <paramref name="index"/>'s aim, up and FoV: at <paramref name="aimPoint"/>, along the path in Direction-of-travel mode, or recorded; <paramref name="evaluator"/> is called only for the path.</summary>
    public static (Vector3 Forward, Vector3 Up, float Fov) Pose(
        Track track,
        int index,
        Vector3? aimPoint,
        Func<TrackEvaluator> evaluator
    )
    {
        var point = track.Points[index];
        var recorded = CameraRotation.FromAngles(point.Yaw, point.Pitch, point.Roll);
        if (aimPoint is { } at && TrackAim.Toward(point.Position, at) is not null)
        {
            var facing = at - point.Position;
            var up = Vector3.Transform(
                CameraRotation.Upright(facing),
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(facing), point.Roll)
            );
            return (facing, up, point.PlayedFov);
        }

        if (track.Aim != AimMode.PathTangent)
            return (CameraRotation.Forward(recorded), CameraRotation.Up(recorded), point.PlayedFov);

        var path = evaluator();
        return path.Evaluate(path.PointSeconds(index)) is { } frame
            ? (frame.LookAt - frame.Position, frame.Up, point.PlayedFov)
            : (CameraRotation.Forward(recorded), CameraRotation.Up(recorded), point.PlayedFov);
    }

    /// <summary>Builds the glyph at <paramref name="apex"/>; <paramref name="fov"/> is vertical, in radians, and <paramref name="depth"/> in yalms.</summary>
    public static CameraGlyph Build(Vector3 apex, Vector3 forward, Vector3 up, float fov, float aspect, float depth)
    {
        var f = Vector3.Normalize(forward);
        var u = up - (f * Vector3.Dot(up, f));
        if (u.LengthSquared() < 1e-8f)
            u = CameraRotation.Upright(f);
        u = Vector3.Normalize(u);
        var side = Vector3.Cross(u, f);

        var halfAngle = float.IsFinite(fov) ? Math.Clamp(fov / 2f, 0f, MaxHalfAngle) : DefaultHalfAngle;
        var halfHeight = depth * MathF.Tan(halfAngle);
        var halfWidth = halfHeight * aspect;
        var centre = apex + (f * depth);
        var top = centre + (u * halfHeight);
        var bottom = centre - (u * halfHeight);
        var tab = MathF.Min(halfWidth, halfHeight);

        return new CameraGlyph(
            apex,
            [
                top - (side * halfWidth),
                top + (side * halfWidth),
                bottom + (side * halfWidth),
                bottom - (side * halfWidth),
            ],
            top - (side * tab * TabHalfWidth),
            top + (u * tab * TabHeight),
            top + (side * tab * TabHalfWidth)
        );
    }
}
