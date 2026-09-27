using System.Numerics;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A rotation blended by distance along the path through each point's rotation, turning at one rate through each point, limited by <see cref="Hermite.KeySlope(IReadOnlyList{float}, IReadOnlyList{Vector3}, int)"/>: <see cref="PathChannel"/> for orientation.</summary>
public sealed class PathRotation
{
    /// <summary>Below this, in radians, a turn is small enough to treat as straight, avoiding a divide by its angle.</summary>
    private const float SmallAngle = 1e-6f;

    private readonly Quaternion[] rotations;
    private readonly float[] distances;
    private readonly Vector3[] turns;
    private readonly Vector3[] rates;

    /// <summary>A rotation through <paramref name="rotations"/>, each at <paramref name="distances"/> along the path, which strictly increase.</summary>
    public PathRotation(IReadOnlyList<Quaternion> rotations, IReadOnlyList<float> distances)
    {
        PathChannel.Validate(rotations.Count, distances);
        this.rotations = [.. rotations];
        this.distances = [.. distances];
        turns = Enumerable.Range(0, rotations.Count).Select(Turn).ToArray();
        rates = Enumerable
            .Range(0, rotations.Count)
            .Select(i => rotations.Count == 1 ? Vector3.Zero : Hermite.KeySlope(distances, turns, i))
            .ToArray();
    }

    /// <summary>The rotation at <paramref name="distance"/> along the path, held before the first point and after the last.</summary>
    public Quaternion At(float distance)
    {
        var n = rotations.Length;
        if (n == 1 || distance <= distances[0])
            return rotations[0];
        if (distance >= distances[n - 1])
            return rotations[n - 1];

        var leg = Search.LastAtOrBelow(distances, distance, 0, n - 1) + 1;
        var span = distances[leg] - distances[leg - 1];
        var u = (distance - distances[leg - 1]) / span;
        var from = rates[leg - 1] * span;
        var to = InverseLeftJacobian(turns[leg], rates[leg] * span);
        var turned = Hermite.At(Vector3.Zero, turns[leg], from, to, u);
        return Quaternion.Normalize(Quaternion.Concatenate(rotations[leg - 1], Exp(turned)));
    }

    /// <summary>Leg <paramref name="leg"/>'s turn as a world rotation vector (axis times angle), the short way round; zero for the first point, which no leg reaches.</summary>
    private Vector3 Turn(int leg)
    {
        if (leg == 0)
            return Vector3.Zero;
        var relative = Quaternion.Concatenate(Quaternion.Inverse(rotations[leg - 1]), rotations[leg]);
        // At half a turn (W = 0, give or take float round-off in cos(π/2)) the sign is kept, so it turns the way the points' angles do.
        return Log(relative.W < -SmallAngle ? Quaternion.Negate(relative) : relative);
    }

    /// <summary>The rotation-vector rate at turn <paramref name="turn"/> that gives world turn rate <paramref name="rate"/>: the inverse left Jacobian of the exponential map; <paramref name="rate"/> unchanged below <see cref="SmallAngle"/>.</summary>
    private static Vector3 InverseLeftJacobian(Vector3 turn, Vector3 rate)
    {
        var angle = turn.Length();
        if (angle < SmallAngle)
            return rate;
        var cross = Vector3.Cross(turn, rate);
        var factor = (1f / (angle * angle)) - ((1f + MathF.Cos(angle)) / (2f * angle * MathF.Sin(angle)));
        return rate - (cross / 2f) + (factor * Vector3.Cross(turn, cross));
    }

    /// <summary>The rotation vector (axis times angle) of a unit quaternion.</summary>
    private static Vector3 Log(Quaternion rotation)
    {
        var axis = new Vector3(rotation.X, rotation.Y, rotation.Z);
        var sine = axis.Length();
        if (sine < SmallAngle)
            return axis * 2f;
        return axis / sine * (2f * MathF.Atan2(sine, rotation.W));
    }

    /// <summary>The unit quaternion turning by a rotation vector (axis times angle).</summary>
    private static Quaternion Exp(Vector3 turn)
    {
        var angle = turn.Length();
        if (angle < SmallAngle)
            return Quaternion.Normalize(new Quaternion(turn / 2f, 1f));
        return Quaternion.CreateFromAxisAngle(turn / angle, angle);
    }
}
