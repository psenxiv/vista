using System.Numerics;
using Vista.Core.Tracks;

namespace Vista.Core.Display;

/// <summary>How fast a track's camera look turns along its path, sampled over time, for drawing the path by turn rate.</summary>
public static class TurnHeat
{
    public const float SamplesPerSecond = 30f;

    /// <summary>Degrees per second drawn fully hot.</summary>
    public const float Hot = 90f;

    /// <summary>The level drawn warm, halfway to <see cref="Hot"/>.</summary>
    public const float Warm = 0.5f;

    /// <summary>A place on the path and how fast the look turns arriving there, in degrees per second.</summary>
    public readonly record struct Sample(Vector3 Position, float DegreesPerSecond);

    /// <summary>The camera's place and turn rate every 1/<see cref="SamplesPerSecond"/> seconds through the track, aimed at <paramref name="target"/> when given; the first sample's rate is 0.</summary>
    public static IReadOnlyList<Sample> Samples(TrackEvaluator evaluator, Vector3? target = null)
    {
        var count = (int)Math.Ceiling(evaluator.Duration * SamplesPerSecond) + 1;
        if (evaluator.Duration <= 0.0 || evaluator.Evaluate(0.0, target) is null) return [];

        var samples = new Sample[count];
        Vector3? before = null;
        for (var i = 0; i < count; i++)
        {
            var time = Math.Min(i / (double)SamplesPerSecond, evaluator.Duration);
            var frame = evaluator.Evaluate(time, target)!.Value;
            var look = Vector3.Normalize(frame.LookAt - frame.Position);
            var span = i == 0 ? 0.0 : time - Math.Min((i - 1) / (double)SamplesPerSecond, evaluator.Duration);
            var rate = before is { } b && span > 0.0 ? (float)(MathF.Acos(Math.Clamp(Vector3.Dot(b, look), -1f, 1f)) * 180f / MathF.PI / span) : 0f;
            samples[i] = new Sample(frame.Position, rate);
            before = look;
        }

        return samples;
    }

    /// <summary>Where <paramref name="degreesPerSecond"/> falls on the scale: 0 at rest, <see cref="Warm"/> halfway, 1 at <see cref="Hot"/> and above.</summary>
    public static float Level(float degreesPerSecond) => Math.Clamp(degreesPerSecond / Hot, 0f, 1f);
}
