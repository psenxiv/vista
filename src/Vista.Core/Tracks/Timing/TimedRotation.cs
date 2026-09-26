using System.Numerics;

namespace Vista.Core.Tracks.Timing;

/// <summary>A rotation blended over time through each point's rotation, held while the point holds, turning at one rate through each point: <see cref="TimedChannel"/> for orientation.</summary>
public sealed class TimedRotation
{
    /// <summary>Below this, in radians, a turn is small enough to treat as straight, avoiding a divide by its angle.</summary>
    private const float SmallAngle = 1e-6f;

    private readonly Quaternion[] rotations;
    private readonly float[] arrive;
    private readonly float[] depart;
    private readonly Vector3[] turns;
    private readonly Vector3[] rates;

    /// <summary>A rotation through <paramref name="rotations"/>, each reached at <paramref name="arrive"/> and left at <paramref name="depart"/>, in seconds.</summary>
    public TimedRotation(IReadOnlyList<Quaternion> rotations, IReadOnlyList<float> arrive, IReadOnlyList<float> depart)
    {
        if (rotations.Count == 0 || arrive.Count != rotations.Count || depart.Count != rotations.Count)
            throw new ArgumentException("A rotation needs an arrival and a departure for every point.");
        this.rotations = [.. rotations];
        this.arrive = [.. arrive];
        this.depart = [.. depart];
        turns = Enumerable.Range(0, rotations.Count).Select(Turn).ToArray();
        rates = Enumerable.Range(0, rotations.Count).Select(Rate).ToArray();
    }

    /// <summary>The rotation at <paramref name="time"/> seconds, held before the first point and after the last.</summary>
    public Quaternion At(double time)
    {
        var n = rotations.Length;
        if (n == 1)
            return rotations[0];
        if (time >= arrive[n - 1])
            return rotations[n - 1];

        var leg = Search.LastAtOrBelow(arrive, time, 0, n - 1) + 1;
        var start = depart[leg - 1];
        if (time <= start)
            return rotations[leg - 1];
        var span = arrive[leg] - start;
        var u = (float)((time - start) / span);
        var from = rates[leg - 1] * span;
        var to = InverseLeftJacobian(turns[leg], rates[leg] * span);
        var turned = new Vector3(
            Hermite.At(0f, turns[leg].X, from.X, to.X, u),
            Hermite.At(0f, turns[leg].Y, from.Y, to.Y, u),
            Hermite.At(0f, turns[leg].Z, from.Z, to.Z, u)
        );
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

    /// <summary>Point <paramref name="i"/>'s turn rate as a world rotation vector per second: 0 through a hold, half the one-sided rate at an end, and the time-weighted Catmull-Rom rate between, as <see cref="TimedChannel"/>'s slope.</summary>
    private Vector3 Rate(int i)
    {
        var n = rotations.Length;
        if (n == 1 || depart[i] - arrive[i] > TimedChannel.MinSeconds)
            return Vector3.Zero;
        if (i == 0)
            return OneSided(1) / 2f;
        if (i == n - 1)
            return OneSided(n - 1) / 2f;

        var before = arrive[i] - depart[i - 1];
        var after = arrive[i + 1] - depart[i];
        if (before <= TimedChannel.MinSeconds || after <= TimedChannel.MinSeconds)
            return Vector3.Zero;
        return ((turns[i] / before * after) + (turns[i + 1] / after * before)) / (before + after);
    }

    /// <summary>Leg <paramref name="leg"/>'s average turn rate per second, or zero when it takes no time.</summary>
    private Vector3 OneSided(int leg)
    {
        var span = arrive[leg] - depart[leg - 1];
        return span <= TimedChannel.MinSeconds ? Vector3.Zero : turns[leg] / span;
    }

    /// <summary>The inverse left Jacobian of the exponential map at rotation vector <paramref name="turn"/> of angle φ, applied to <paramref name="rate"/>: the world rate that arrives with rotation-vector rate <paramref name="rate"/> at a leg turning by <paramref name="turn"/>. Below <see cref="SmallAngle"/> it's <paramref name="rate"/> unchanged.</summary>
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
