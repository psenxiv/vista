using System.Numerics;

namespace Vista.Core.Tracks.Aiming;

/// <summary>Eases a target position towards the live one with a time constant of smoothing × <see cref="SecondsPerSmoothing"/>.</summary>
public sealed class AimSmoother
{
    /// <summary>Seconds of time constant at smoothing 1.</summary>
    public const float SecondsPerSmoothing = 0.5f;

    private Vector3? current;

    /// <summary>Moves towards <paramref name="target"/> by <paramref name="dt"/> seconds: lands on it when fresh or at smoothing 0, and holds with no time.</summary>
    public Vector3 Step(Vector3 target, float dt, float smoothing)
    {
        if (current is not { } from)
        {
            current = target;
            return target;
        }

        if (dt <= 0f)
            return from;
        var factor = Factor(dt, smoothing);
        var next = factor == 1f ? target : Vector3.Lerp(from, target, factor);
        current = next;
        return next;
    }

    /// <summary>The share of the way to its target an eased value moves in <paramref name="dt"/> seconds at <paramref name="smoothing"/>: 1 at smoothing 0, 0 with no time.</summary>
    public static float Factor(float dt, float smoothing)
    {
        if (dt <= 0f)
            return 0f;
        var timeConstant = Fraction.Clamp(smoothing) * SecondsPerSmoothing;
        return timeConstant <= 0f ? 1f : 1f - MathF.Exp(-dt / timeConstant);
    }

    /// <summary>Sets where the next step eases from.</summary>
    public void Seed(Vector3 position) => current = position;

    /// <summary>Forgets where it was, so the next step lands on its target.</summary>
    public void Reset() => current = null;
}
