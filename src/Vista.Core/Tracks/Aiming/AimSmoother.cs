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

        if (dt <= 0f) return from;
        var timeConstant = Math.Clamp(smoothing, 0f, 1f) * SecondsPerSmoothing;
        var next = timeConstant <= 0f ? target : Vector3.Lerp(from, target, 1f - MathF.Exp(-dt / timeConstant));
        current = next;
        return next;
    }

    /// <summary>Sets where the next step eases from.</summary>
    public void Seed(Vector3 position) => current = position;

    /// <summary>Forgets where it was, so the next step lands on its target.</summary>
    public void Reset() => current = null;
}
