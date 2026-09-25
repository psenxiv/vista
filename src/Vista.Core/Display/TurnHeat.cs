using System.Numerics;
using Vista.Core.Camera;
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

    /// <summary>Yalms a watched character moves before its heat is worked out again.</summary>
    private const float TargetStep = 0.25f;

    /// <summary>A place on the path and how fast the look turns arriving there, in degrees per second.</summary>
    public readonly record struct Sample(Vector3 Position, float DegreesPerSecond);

    /// <summary>The camera's place and turn rate at even steps through the track, at most 1/<see cref="SamplesPerSecond"/> seconds apart, aimed at <paramref name="target"/> when given; the first sample's rate is 0.</summary>
    public static IReadOnlyList<Sample> Samples(TrackEvaluator evaluator, Vector3? target = null)
    {
        if (evaluator.Duration <= 0.0 || evaluator.Evaluate(0.0, target) is null)
            return [];

        // Even steps, so the last isn't a sliver whose float noise, over next to no time, reads as a whip.
        var steps = (int)Math.Ceiling(evaluator.Duration * SamplesPerSecond);
        var span = evaluator.Duration / steps;
        var samples = new Sample[steps + 1];
        Vector3? before = null;
        for (var i = 0; i <= steps; i++)
        {
            var time = i == steps ? evaluator.Duration : i * span;
            var frame = evaluator.Evaluate(time, target)!.Value;
            var look = Vector3.Normalize(frame.LookAt - frame.Position);
            var rate = before is { } b
                ? (float)(Angles.Degrees(MathF.Acos(Math.Clamp(Vector3.Dot(b, look), -1f, 1f))) / span)
                : 0f;
            samples[i] = new Sample(frame.Position, rate);
            before = look;
        }

        return samples;
    }

    /// <summary>Where <paramref name="degreesPerSecond"/> falls on the scale: 0 at rest, <see cref="Warm"/> halfway, 1 at <see cref="Hot"/> and above.</summary>
    public static float Level(float degreesPerSecond) => Math.Clamp(degreesPerSecond / Hot, 0f, 1f);

    /// <summary>True when the aim point appeared, went, or moved more than <see cref="TargetStep"/> yalms.</summary>
    public static bool TargetMoved(Vector3? before, Vector3? now) =>
        before is { } a && now is { } b ? Vector3.Distance(a, b) > TargetStep : before.HasValue != now.HasValue;

    /// <summary>The colour for heat <paramref name="level"/>: <paramref name="restColour"/> at rest, <paramref name="warmColour"/> at <see cref="Warm"/> and <paramref name="hotColour"/> at 1, blended between.</summary>
    public static uint Colour(float level, uint restColour, uint warmColour, uint hotColour) =>
        level <= Warm
            ? Blend(restColour, warmColour, level / Warm)
            : Blend(warmColour, hotColour, (level - Warm) / (1f - Warm));

    /// <summary>Two ImGui colours mixed channel by channel, <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static uint Blend(uint a, uint b, float t)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var from = (a >> shift) & 0xFF;
            var to = (b >> shift) & 0xFF;
            result |= (uint)MathF.Round(from + ((to - (float)from) * t)) << shift;
        }

        return result;
    }
}
