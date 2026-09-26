using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A shot's up, worked out once: level, upright or inverted, except through each vertical passage, or its wider turn span, where it turns once from level to level.</summary>
public sealed class LevelUp
{
    /// <summary>The most seconds between the samples the facing is read at to find the passages, before refining.</summary>
    private const double StepSeconds = 0.1;

    /// <summary>A step that crosses a passage's edge is halved until it's no longer than this, so each passage starts and ends within a millisecond of its edge.</summary>
    public const float EdgeSeconds = 0.001f;

    /// <summary>A shot starting with a sideways part shorter than this, within 0.006° of straight up or down, has no level to start from.</summary>
    private const float Vertical = 1e-4f;

    /// <summary>A facing whose sideways part is shorter than this, within 15° of straight up or down, is in a vertical passage, where level is only the heading and swings round fast.</summary>
    public static readonly float PassageSideways = MathF.Sin(15f * Angles.Degree);

    /// <summary>A turn span reaches no further than this sideways part, 60° from straight up or down, since a level lean squared to a flatter view shrinks below half its length and can roll the picture.</summary>
    public static readonly float SpanSideways = MathF.Sin(60f * Angles.Degree);

    /// <summary>A step where the facing turns more than this, 5°, is halved until it doesn't, so a facing whipping through straight up can't cross a passage between samples.</summary>
    private const float MostTurnPerSample = 5f * Angles.Degree;

    /// <summary>The most times a step is halved: 0.1 s down to about 1.5 µs, past float time's resolution over a long shot.</summary>
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

    /// <summary>The up along a shot of <paramref name="duration"/> seconds facing <paramref name="facing"/> (null keeps the facing before), inverting over loops when <paramref name="allowInverted"/>; a shot starting straight up takes <paramref name="verticalStartUp"/>, negated straight down; given the <paramref name="points"/>' times, each passage turns over its turn span.</summary>
    public static LevelUp Along(
        Func<double, Vector3?> facing,
        float duration,
        bool allowInverted,
        Vector3 verticalStartUp,
        (float[] Arrive, float[] Depart)? points = null
    )
    {
        var pointTimes = points is { } times ? times.Arrive.Concat(times.Depart) : [];
        var steps = Enumerable
            .Range(0, (int)Math.Ceiling(duration / StepSeconds) + 1)
            .Select(k => (float)Math.Min(k * StepSeconds, duration))
            .Concat(pointTimes.Where(t => t >= 0f && t <= duration))
            .Distinct()
            .Order()
            .ToArray();
        Vector3 FacingAt(float time, Vector3 before) => facing(time) is { } f ? Vectors.NormalizeOr(f, before) : before;
        bool CrossesEdge(Vector3 from, Vector3 to) =>
            InPassage(from) != InPassage(to) || (points is not null && InSpanReach(from) != InSpanReach(to));

        var samples = new List<(float Time, Vector3 Facing)>
        {
            (steps[0], FacingAt(steps[0], new Vector3(0f, 0f, -1f))),
        };
        void Refine(float from, Vector3 fromFacing, float to, Vector3 toFacing, int halvings)
        {
            var middle = (from + to) / 2f;
            var crossesEdge = CrossesEdge(fromFacing, toFacing) && to - from > EdgeSeconds;
            if (
                halvings < MostHalvings
                && middle > from
                && middle < to
                && (crossesEdge || Vectors.AngleBetween(fromFacing, toFacing) > MostTurnPerSample)
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

        if (points is { } timed)
            AddSplits(samples, FacingAt, timed, duration);
        var sampleTimes = samples.Select(s => s.Time).ToArray();
        var sampleFacings = samples.Select(s => s.Facing).ToArray();
        var sampleTurned = new float[sampleTimes.Length];
        for (var k = 1; k < sampleTimes.Length; k++)
            sampleTurned[k] = sampleTurned[k - 1] + Vectors.AngleBetween(sampleFacings[k - 1], sampleFacings[k]);

        return new LevelUp(
            sampleTimes,
            sampleFacings,
            sampleTurned,
            Plan(sampleTimes, sampleFacings, duration, allowInverted, verticalStartUp, points)
        );
    }

    /// <summary>Adds a sample at each <see cref="Split"/> in <paramref name="samples"/>, so neighbouring turn spans meet exactly there.</summary>
    private static void AddSplits(
        List<(float Time, Vector3 Facing)> samples,
        Func<float, Vector3, Vector3> facingAt,
        (float[] Arrive, float[] Depart) points,
        float duration
    )
    {
        var sampledTimes = samples.Select(s => s.Time).ToArray();
        var sampledFacings = samples.Select(s => s.Facing).ToArray();
        var extents = Extents(sampledFacings);
        var reaches = Reaches(sampledTimes, sampledFacings, extents, points, duration);
        foreach (
            var split in Enumerable
                .Range(0, extents.Count)
                .Select(i => Split(sampledTimes, extents, reaches, i))
                .OfType<float>()
        )
        {
            var before = samples.FindLastIndex(s => s.Time <= split);
            if (samples[before].Time < split)
                samples.Insert(before + 1, (split, facingAt(split, samples[before].Facing)));
        }
    }

    /// <summary>Each passage's planned turn along the sampled <paramref name="times"/> and <paramref name="facings"/>, over its turn span when <paramref name="points"/>' times are given.</summary>
    private static Passage[] Plan(
        float[] times,
        Vector3[] facings,
        float duration,
        bool allowInverted,
        Vector3 verticalStartUp,
        (float[] Arrive, float[] Depart)? points
    )
    {
        var extents = Extents(facings);
        var reaches = points is { } timed ? Reaches(times, facings, extents, timed, duration) : null;
        var found = new List<Passage>();
        var inverted = false;
        var previousEnd = 0;
        for (var i = 0; i < extents.Count; i++)
        {
            var (start, end, pole, inside) = extents[i];
            if (end is not { } last)
            {
                var held =
                    inside && CameraRotation.Sideways(facings[0]) < Vertical
                        ? Flat(pole * verticalStartUp)
                        : Level(facings[start], inverted, pole);
                found.Add(new Passage(start, null, held, 0f, inverted, inverted, FromLevel: false));
                break;
            }

            if (inside)
            {
                // A shot that starts inside a passage has no picture before it to turn from, so it starts as it leaves.
                found.Add(
                    new Passage(
                        start,
                        last,
                        Level(facings[last], inverted, pole),
                        0f,
                        inverted,
                        inverted,
                        FromLevel: false
                    )
                );
                previousEnd = last;
                continue;
            }

            if (reaches?[i] is { } reach)
            {
                var next =
                    Split(times, extents, reaches, i) is { } split ? SampleAt(times, split)
                    : i + 1 < extents.Count ? extents[i + 1].Start
                    : times.Length - 1;
                start = Math.Max(reach.Start, previousEnd);
                last = Math.Min(reach.End, next);
            }

            var from = Level(facings[start], inverted, pole);
            var to = Level(facings[last], inverted, pole);
            var after = inverted;
            if (allowInverted && Vector3.Dot(from, to) < Reversed)
            {
                after = !inverted;
                to = -to;
            }

            var turn = Vectors.SignedAngle(from, to, Vector3.UnitY);
            found.Add(new Passage(start, last, from, turn, inverted, after, FromLevel: true));
            inverted = after;
            previousEnd = last;
        }

        return [.. found];
    }

    /// <summary>The vertical passages along the sampled <paramref name="facings"/>.</summary>
    private static List<Extent> Extents(Vector3[] facings)
    {
        var extents = new List<Extent>();
        for (var k = 0; k < facings.Length; k++)
        {
            if (!InPassage(facings[k]))
                continue;
            var end = k;
            while (end < facings.Length && InPassage(facings[end]))
                end++;
            extents.Add(
                new Extent(k == 0 ? 0 : k - 1, end == facings.Length ? null : end, MathF.Sign(facings[k].Y), k == 0)
            );
            k = end;
        }

        return extents;
    }

    /// <summary>Each passage's turn span before its neighbours limit it: from the last point left before it to the first reached after, within 60° of its pole; null for one the shot starts inside or never leaves.</summary>
    private static (int Start, int End)?[] Reaches(
        float[] times,
        Vector3[] facings,
        List<Extent> extents,
        (float[] Arrive, float[] Depart) points,
        float duration
    )
    {
        bool Steep(int sample, float pole) => InSpanReach(facings[sample]) && MathF.Sign(facings[sample].Y) == pole;
        var reaches = new (int Start, int End)?[extents.Count];
        for (var i = 0; i < extents.Count; i++)
        {
            if (extents[i] is not { Inside: false, End: { } end } extent)
                continue;
            var start = extent.Start;
            while (start > 0 && Steep(start, extent.Pole) && Steep(start - 1, extent.Pole))
                start--;
            var last = end;
            while (last < times.Length - 1 && Steep(last, extent.Pole) && Steep(last + 1, extent.Pole))
                last++;
            var departed = points.Depart.Where(t => t <= times[extent.Start]).DefaultIfEmpty(0f).Max();
            var arrived = points.Arrive.Where(t => t >= times[end]).DefaultIfEmpty(duration).Min();
            reaches[i] = (Math.Max(SampleAt(times, departed), start), Math.Min(SampleAt(times, arrived), last));
        }

        return reaches;
    }

    /// <summary>Where passage <paramref name="i"/>'s turn span and passage <paramref name="i"/> + 1's would overlap: halfway from passage <paramref name="i"/>'s end to passage <paramref name="i"/> + 1's start; null when they don't overlap.</summary>
    private static float? Split(float[] times, List<Extent> extents, (int Start, int End)?[] reaches, int i) =>
        i + 1 < extents.Count && reaches[i] is { } first && reaches[i + 1] is { } second && first.End > second.Start
            ? (times[extents[i].End!.Value] + times[extents[i + 1].Start]) / 2f
            : null;

    /// <summary>The sample at or just before <paramref name="time"/>: exactly at it for a point's time or a split, which are always sampled.</summary>
    private static int SampleAt(float[] times, float time) => Search.LastAtOrBelow(times, time, 0, times.Length);

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

        var share = Fraction.Between(
            Turned(time, forward),
            turned[passage.Start],
            passage.End is { } last ? turned[last] : turned[^1],
            0f
        );
        if (share <= 0f && passage.FromLevel)
            return passage.InvertedBefore ? -Level(forward) : Level(forward);

        // Eased at both ends, so the picture starts and stops turning gently. Any level lean squared to a steep facing is an
        // up for it: squared to the facing it leans from, it's that facing's level up exactly.
        var eased = Hermite.Smoothstep(share);
        var lean = Vector3.Transform(passage.From, Quaternion.CreateFromAxisAngle(Vector3.UnitY, passage.Turn * eased));
        return CameraRotation.SquareUp(lean, forward);
    }

    /// <summary>How far, in radians, the facing has turned from the shot's start to <paramref name="time"/>, facing unit <paramref name="forward"/>.</summary>
    private float Turned(double time, Vector3 forward)
    {
        var index = Search.LastAtOrBelow(times, time, 0, times.Length);
        return turned[index] + Vectors.AngleBetween(facings[index], forward);
    }

    /// <summary>Whether unit <paramref name="forward"/> is within a vertical passage.</summary>
    private static bool InPassage(Vector3 forward) => CameraRotation.Sideways(forward) < PassageSideways;

    /// <summary>Whether unit <paramref name="forward"/> is steep enough for a turn span to reach.</summary>
    private static bool InSpanReach(Vector3 forward) => CameraRotation.Sideways(forward) < SpanSideways;

    /// <summary>The upright up facing unit <paramref name="forward"/>.</summary>
    private static Vector3 Level(Vector3 forward) => CameraRotation.Upright(forward);

    /// <summary>The level way the up leans facing unit <paramref name="forward"/> at a passage toward <paramref name="pole"/> (1 up, -1 down): back from a climb and ahead into a dive, reversed when <paramref name="inverted"/>.</summary>
    private static Vector3 Level(Vector3 forward, bool inverted, float pole) =>
        (inverted ? pole : -pole) * Flat(forward);

    /// <summary><paramref name="v"/>'s level part as a unit vector; straight up or down, yaw 0's heading, −z.</summary>
    private static Vector3 Flat(Vector3 v) => Vectors.FlatOr(v, new Vector3(0f, 0f, -1f));

    /// <summary>A vertical passage from the sample before it, <see cref="Start"/>, to the first after it, <see cref="End"/> (null if the shot ends in it), toward <see cref="Pole"/> (1 up, -1 down); <see cref="Inside"/> when the shot starts in it.</summary>
    private readonly record struct Extent(int Start, int? End, float Pole, bool Inside);

    /// <summary>A vertical passage's turn from sample <see cref="Start"/> to sample <see cref="End"/> (null if the shot ends in it), over the passage or its turn span, turning up about the vertical by <see cref="Turn"/> from leaning along <see cref="From"/>; <see cref="FromLevel"/> when it starts from a level sample.</summary>
    private readonly record struct Passage(
        int Start,
        int? End,
        Vector3 From,
        float Turn,
        bool InvertedBefore,
        bool InvertedAfter,
        bool FromLevel
    );
}
