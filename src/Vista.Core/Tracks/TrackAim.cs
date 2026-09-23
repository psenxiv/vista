using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Where a playing camera looks: the recorded aim at each point, or the direction of travel.</summary>
public static class TrackAim
{
    /// <summary>Steepest pitch a path-tangent or aim-key look can reach, so it never gimbals straight up or down.</summary>
    public const float PitchLimit = 89f * MathF.PI / 180f;

    /// <summary>Closer than this, in yalms, a target gives no aim.</summary>
    public const float MinTargetDistance = 0.1f;

    /// <summary>Below this, a direction vector is treated as undefined rather than normalised.</summary>
    private const float DirectionEpsilon = 1e-6f;

    /// <summary>How far to step off a segment boundary when the exact boundary derivative is degenerate: at either end of the path, or beside a collapsed segment's phantom point.</summary>
    private const float BoundaryNudge = 1e-3f;

    /// <summary>Yaw and pitch of a view direction; the exact inverse of <c>FreeCamMotion</c>'s direction convention.</summary>
    public static (float Yaw, float Pitch) FromDirection(Vector3 direction)
    {
        var normalized = Vector3.Normalize(direction);
        var yaw = MathF.Atan2(-normalized.X, -normalized.Z);
        var pitch = MathF.Asin(Math.Clamp(normalized.Y, -1f, 1f));
        return (yaw, pitch);
    }

    /// <summary>The pitch-clamped aim from <paramref name="from"/> at <paramref name="target"/>, or null when it is closer than <see cref="MinTargetDistance"/>.</summary>
    public static (float Yaw, float Pitch)? Toward(Vector3 from, Vector3 target)
    {
        var direction = target - from;
        return direction.Length() < MinTargetDistance ? null : ClampPitch(FromDirection(direction));
    }

    /// <summary>The pitch-clamped aim along <paramref name="direction"/>, or null when it's too short to give one.</summary>
    public static (float Yaw, float Pitch)? Along(Vector3 direction)
        => direction.LengthSquared() <= DirectionEpsilon * DirectionEpsilon ? null : ClampPitch(FromDirection(direction));

    /// <summary>Walks an angle sequence (yaw or roll), adding or subtracting full turns so consecutive values differ by at most π.</summary>
    public static float[] UnwrapAngles(IReadOnlyList<float> yaws)
    {
        var result = new float[yaws.Count];
        if (yaws.Count == 0) return result;

        result[0] = yaws[0];
        for (var i = 1; i < yaws.Count; i++)
        {
            result[i] = result[i - 1] + Angles.Delta(result[i - 1], yaws[i]);
        }

        return result;
    }

    /// <summary>The path's direction of travel, pitch-clamped, falling back to the nearest valid direction where coincident points collapse the derivative.</summary>
    public static (float Yaw, float Pitch) PathTangent(
        IReadOnlyList<Vector3> points, ArcLengthTable table, int segment, float fraction, (float Yaw, float Pitch) fallback)
    {
        var direct = TryDirection(points, table, segment, fraction);
        if (direct is { } direction) return ClampPitch(FromDirection(direction));

        var nearest = NearestValidDirection(points, table, segment, fraction);
        return nearest is { } found ? ClampPitch(FromDirection(found)) : fallback;
    }

    /// <summary>The derivative at this exact place, or null where the segment has collapsed to zero length.</summary>
    private static Vector3? TryDirection(IReadOnlyList<Vector3> points, ArcLengthTable table, int segment, float fraction)
    {
        if (table.SegmentCount == 0 || table.SegmentLength(segment) <= DirectionEpsilon) return null;

        // The path's speed falls to zero at its two ends, where the direction is rounding noise, so it is read a nudge inside.
        var t = table.ParameterAt(segment, fraction);
        if (segment == 0) t = MathF.Max(t, BoundaryNudge);
        if (segment == table.SegmentCount - 1) t = MathF.Min(t, 1f - BoundaryNudge);
        var derivative = CatmullRom.Derivative(points, segment, t);
        return derivative.LengthSquared() > DirectionEpsilon * DirectionEpsilon ? derivative : null;
    }

    /// <summary>Searches every other segment by arc distance from this place and returns the closest one with a defined direction, or null if none exists.</summary>
    private static Vector3? NearestValidDirection(IReadOnlyList<Vector3> points, ArcLengthTable table, int segment, float fraction)
    {
        var segmentCount = table.SegmentCount;
        if (segmentCount == 0) return null;

        var cumulative = new float[segmentCount + 1];
        for (var i = 0; i < segmentCount; i++)
            cumulative[i + 1] = cumulative[i] + table.SegmentLength(i);

        var queryPosition = cumulative[segment] + (Math.Clamp(fraction, 0f, 1f) * table.SegmentLength(segment));

        var bestDistance = float.PositiveInfinity;
        Vector3? best = null;

        for (var i = 0; i < segmentCount; i++)
        {
            var length = table.SegmentLength(i);
            if (length <= DirectionEpsilon) continue;

            float t;
            float distance;
            if (queryPosition <= cumulative[i])
            {
                t = 0f;
                distance = cumulative[i] - queryPosition;
            }
            else if (queryPosition >= cumulative[i + 1])
            {
                t = 1f;
                distance = queryPosition - cumulative[i + 1];
            }
            else
            {
                t = table.ParameterAt(i, (queryPosition - cumulative[i]) / length);
                distance = 0f;
            }

            if (distance >= bestDistance) continue;

            var derivative = CatmullRom.Derivative(points, i, t);
            if (derivative.LengthSquared() <= DirectionEpsilon * DirectionEpsilon && (t == 0f || t == 1f))
            {
                // The exact boundary can inherit a neighbouring collapsed segment's phantom point; step just inside.
                t = t == 0f ? BoundaryNudge : 1f - BoundaryNudge;
                derivative = CatmullRom.Derivative(points, i, t);
            }

            if (derivative.LengthSquared() <= DirectionEpsilon * DirectionEpsilon) continue;

            bestDistance = distance;
            best = derivative;
        }

        return best;
    }

    private static (float Yaw, float Pitch) ClampPitch((float Yaw, float Pitch) aim)
        => (aim.Yaw, Math.Clamp(aim.Pitch, -PitchLimit, PitchLimit));
}
