using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A camera's up along a shot, worked out once and read back at any time: level (upright, or inverted while the track is upside down) except through each vertical passage, where the picture makes one planned turn from level at the passage's start to level at its end.</summary>
public sealed class LevelUp
{
    /// <summary>The most seconds between the samples the facing is read at to find the passages.</summary>
    public const double StepSeconds = 0.01;

    /// <summary>A facing whose sideways part is shorter than this, within 15° of straight up or down, is in a vertical passage, where level is only the heading and swings round fast.</summary>
    public static readonly float PassageSideways = MathF.Sin(15f * MathF.PI / 180f);

    /// <summary>A step where the facing turns more than this, 5°, is halved until it doesn't, so a facing whipping through straight up can't cross a passage between samples.</summary>
    private const float MostTurnPerSample = 5f * MathF.PI / 180f;

    /// <summary>The most times a step is halved: 10 ms down to about 0.15 µs, past float time's resolution over a long shot.</summary>
    private const int MostHalvings = 16;

    /// <summary>A passage whose way out is reversed from its way in by more than 135° (a dot product below -cos 45°), as over the top of a loop, turns the track upside down, or back upright.</summary>
    private static readonly float Reversed = -MathF.Cos(MathF.PI / 4f);

    private readonly float[] times;
    private readonly Vector3[] facings;
    private readonly float[] turned;
    private readonly Passage[] passages;
    private readonly float[] passageStarts;

    private LevelUp(float[] times, Vector3[] facings, float[] turned, Passage[] passages)
    {
        this.times = times;
        this.facings = facings;
        this.turned = turned;
        this.passages = passages;
        passageStarts = [.. passages.Select(p => times[p.Start])];
    }

    /// <summary>The up along a shot of <paramref name="duration"/> seconds facing <paramref name="facing"/> (null keeps the facing before), sampled at <see cref="StepSeconds"/>, more finely where the facing turns fast, and at <paramref name="breaks"/>, the shot's key times. Passages turn it upside down only when <paramref name="allowInverted"/>; a shot that starts facing straight up takes <paramref name="verticalStartUp"/>, negated facing straight down.</summary>
    public static LevelUp Along(
        Func<double, Vector3?> facing,
        IEnumerable<float> breaks,
        float duration,
        bool allowInverted,
        Vector3 verticalStartUp
    )
    {
        var steps = Enumerable
            .Range(0, (int)Math.Ceiling(duration / StepSeconds) + 1)
            .Select(k => (float)Math.Min(k * StepSeconds, duration))
            .Concat(breaks.Where(t => t > 0f && t < duration))
            .Distinct()
            .Order()
            .ToArray();
        Vector3 FacingAt(float time, Vector3 before) =>
            facing(time) is { } f && f != Vector3.Zero ? Vector3.Normalize(f) : before;

        var samples = new List<(float Time, Vector3 Facing)>
        {
            (steps[0], FacingAt(steps[0], new Vector3(0f, 0f, -1f))),
        };
        void Refine(float from, Vector3 fromFacing, float to, Vector3 toFacing, int halvings)
        {
            var middle = (from + to) / 2f;
            if (
                halvings < MostHalvings
                && middle > from
                && middle < to
                && Angle(fromFacing, toFacing) > MostTurnPerSample
            )
            {
                var middleFacing = FacingAt(middle, fromFacing);
                Refine(from, fromFacing, middle, middleFacing, halvings + 1);
                Refine(middle, middleFacing, to, toFacing, halvings + 1);
                return;
            }

            samples.Add((to, toFacing));
        }

        for (var k = 1; k < steps.Length; k++)
        {
            var (from, fromFacing) = samples[^1];
            Refine(from, fromFacing, steps[k], FacingAt(steps[k], fromFacing), 0);
        }

        var sampleTimes = samples.Select(s => s.Time).ToArray();
        var sampleFacings = samples.Select(s => s.Facing).ToArray();
        var sampleTurned = new float[sampleTimes.Length];
        for (var k = 1; k < sampleTimes.Length; k++)
            sampleTurned[k] = sampleTurned[k - 1] + Angle(sampleFacings[k - 1], sampleFacings[k]);

        var found = new List<Passage>();
        var inverted = false;
        for (var k = 0; k < sampleTimes.Length; k++)
        {
            if (!InPassage(sampleFacings[k]))
                continue;
            var start = k == 0 ? 0 : k - 1;
            var end = k;
            while (end < sampleTimes.Length && InPassage(sampleFacings[end]))
                end++;
            if (end == sampleTimes.Length)
            {
                var (held, heldTilt) =
                    start == k
                        ? Tilted(sampleFacings[0].Y < 0f ? -verticalStartUp : verticalStartUp)
                        : Level(sampleFacings[start], inverted);
                found.Add(new Passage(start, null, held, heldTilt, 0f, heldTilt, inverted, inverted, FromLevel: false));
                break;
            }

            var (to, toTilt) = Level(sampleFacings[end], inverted);
            if (start == k)
            {
                // A shot that starts inside a passage has no picture before it to turn from, so it starts as it leaves.
                found.Add(new Passage(start, end, to, toTilt, 0f, toTilt, inverted, inverted, FromLevel: false));
                k = end;
                continue;
            }

            var (from, fromTilt) = Level(sampleFacings[start], inverted);
            var after = inverted;
            if (allowInverted && Vector3.Dot(from, to) < Reversed)
            {
                after = !inverted;
                (to, toTilt) = (-to, -toTilt);
            }

            var turn = MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), Vector3.UnitY), Vector3.Dot(from, to));
            found.Add(new Passage(start, end, from, fromTilt, turn, toTilt, inverted, after, FromLevel: true));
            inverted = after;
            k = end;
        }

        return new LevelUp(sampleTimes, sampleFacings, sampleTurned, [.. found]);
    }

    /// <summary>The up at <paramref name="time"/> facing <paramref name="facing"/>.</summary>
    public Vector3 At(double time, Vector3 facing)
    {
        var forward = Vector3.Normalize(facing);
        var index = passages.Length == 0 ? -1 : Search.LastAtOrBelow(passageStarts, time, 0, passages.Length);
        if (index < 0 || time < passageStarts[index])
            return Level(forward);
        var passage = passages[index];
        if (passage.End is { } end && time >= times[end])
            return passage.InvertedAfter ? -Level(forward) : Level(forward);

        var span = (passage.End is { } last ? turned[last] : turned[^1]) - turned[passage.Start];
        var share = span > 0f ? Math.Clamp((Turned(time, forward) - turned[passage.Start]) / span, 0f, 1f) : 0f;
        if (share <= 0f && passage.FromLevel)
            return passage.InvertedBefore ? -Level(forward) : Level(forward);

        // Eased at both ends, so the picture starts and stops turning gently.
        var eased = share * share * (3f - (2f * share));
        var heading = Vector3.Transform(
            passage.From,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, passage.Turn * eased)
        );
        var tilt = passage.FromTilt + ((passage.ToTilt - passage.FromTilt) * eased);
        return Square((heading * MathF.Cos(tilt)) + (Vector3.UnitY * MathF.Sin(tilt)), forward);
    }

    /// <summary>How far, in radians, the facing has turned from the shot's start to <paramref name="time"/>, facing unit <paramref name="forward"/>.</summary>
    private float Turned(double time, Vector3 forward)
    {
        var index = Search.LastAtOrBelow(times, time, 0, times.Length);
        return turned[index] + Angle(facings[index], forward);
    }

    /// <summary>Whether unit <paramref name="forward"/> is within a vertical passage.</summary>
    private static bool InPassage(Vector3 forward) => Sideways(forward) < PassageSideways;

    /// <summary>The length of unit <paramref name="forward"/>'s level part.</summary>
    private static float Sideways(Vector3 forward) => MathF.Sqrt((forward.X * forward.X) + (forward.Z * forward.Z));

    /// <summary>The angle between two unit vectors, precise when they're close.</summary>
    private static float Angle(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));

    /// <summary>The upright up facing unit <paramref name="forward"/>.</summary>
    private static Vector3 Level(Vector3 forward) => CameraRotation.Upright(forward);

    /// <summary>Level facing unit <paramref name="forward"/>, inverted when <paramref name="inverted"/>, as the level way it leans and its tilt up from level, in radians.</summary>
    private static (Vector3 Heading, float Tilt) Level(Vector3 forward, bool inverted)
    {
        var (heading, tilt) = Tilted(Level(forward));
        return inverted ? (-heading, -tilt) : (heading, tilt);
    }

    /// <summary><paramref name="up"/> as the level way it leans and its tilt up from level, in radians; straight up leans along -z.</summary>
    private static (Vector3 Heading, float Tilt) Tilted(Vector3 up)
    {
        var level = new Vector3(up.X, 0f, up.Z);
        var length = level.Length();
        return (length > 1e-6f ? level / length : new Vector3(0f, 0f, -1f), MathF.Atan2(up.Y, length));
    }

    /// <summary><paramref name="up"/> made square to unit <paramref name="forward"/> and unit length, or upright if it lies along the forward.</summary>
    private static Vector3 Square(Vector3 up, Vector3 forward)
    {
        var squared = up - (forward * Vector3.Dot(up, forward));
        return squared.LengthSquared() > 1e-12f ? Vector3.Normalize(squared) : Level(forward);
    }

    /// <summary>A vertical passage from sample <see cref="Start"/>, the last level sample before it (or the shot's start), to sample <see cref="End"/>, the first after it (null if the shot ends inside it). Up turns about the vertical by <see cref="Turn"/> from leaning along <see cref="From"/>, while its tilt goes from <see cref="FromTilt"/> to <see cref="ToTilt"/>; <see cref="FromLevel"/> is whether it starts from a level sample, whose up it keeps until the facing moves.</summary>
    private readonly record struct Passage(
        int Start,
        int? End,
        Vector3 From,
        float FromTilt,
        float Turn,
        float ToTilt,
        bool InvertedBefore,
        bool InvertedAfter,
        bool FromLevel
    );
}
