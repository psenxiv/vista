using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using static Vista.Tests.Fixtures;

namespace Vista.Tests;

/// <summary>Playing tracks in tests and measuring how they move: runs, the clocks that step through them, and their measures.</summary>
internal static class TrackRuns
{
    /// <summary>A 60 fps frame, in seconds.</summary>
    internal const double FrameSeconds = 1.0 / 60.0;

    /// <summary>A millisecond, in seconds: the fine fixed step.</summary>
    internal const double Millisecond = 1e-3;

    /// <summary>Seconds a channel's change is first measured over when looking for steps.</summary>
    private const double StepWindow = 0.01;

    /// <summary>Halvings of a window that changes more than its floor, each keeping the half that changes more.</summary>
    private const int StepHalvings = 8;

    /// <summary>The facing's step floor, 0.3°, as the distance between unit directions, 2·sin(θ/2), which is θ to within 1e-7 here.</summary>
    private const float FacingStepFloor = 0.3f * Deg;

    /// <summary>The position's step floor, in yalms.</summary>
    private const float PositionStepFloor = 0.05f;

    /// <summary>The field of view's step floor, 0.1° in radians.</summary>
    private const float FovStepFloor = 0.1f * Deg;

    /// <summary>The smallest change in the picture's up, as the distance between unit ups, looked at for a step: 0.1°.</summary>
    private const float UpStepFloor = 0.1f * Deg;

    /// <summary>The most the picture may turn about its own centre from one 60 fps frame to the next beyond what keeping level asks of it (<see cref="SpinPerTurn"/>): 360° a second.</summary>
    internal const float PictureSpinLimit = 6f * Deg;

    /// <summary>How many times as far as the view itself turns the picture may turn to keep level near straight up: 10, above the 9 of a half turn planned over a vertical passage (half a turn over 30° of view, peaking at 1.5 times that average as it eases).</summary>
    internal const float SpinPerTurn = 10f;

    /// <summary>A sudden change in a channel: when it happens and how far it jumps.</summary>
    internal readonly record struct Step(double Time, float Size);

    /// <summary>A frame a clock played, if the player gave one, and the time it was played at.</summary>
    internal readonly record struct Played(CameraState? Frame, double Time);

    /// <summary>A track, or hand-made motion, played over time: its frame at any moment, how long it lasts, and when it reaches and leaves each point.</summary>
    internal sealed class Run
    {
        private readonly Func<double, CameraState> frame;
        private readonly double[] arrive;
        private readonly double[] depart;

        /// <summary>A track played as the evaluator plays it, aimed at the track's own aim point.</summary>
        internal Run(Track track)
        {
            var evaluator = new TrackEvaluator(track);
            var target = AimTracker.AimPoint(track, null);
            frame = t => evaluator.Evaluate(t, target)!.Value;
            Duration = evaluator.Duration;
            var points = Enumerable.Range(0, track.Points.Count).ToArray();
            arrive = [.. points.Select(p => (double)evaluator.PointSeconds(p))];
            depart =
            [
                .. points.Select(p =>
                    (double)
                        evaluator
                            .Keys[TrackEditing.PointKey(track, p) + (TrackEditing.HoldSeconds(track, p) > 0f ? 1 : 0)]
                            .Time
                ),
            ];
        }

        /// <summary>Hand-made motion of <paramref name="duration"/> seconds, with no points.</summary>
        internal Run(Func<double, CameraState> frame, double duration)
        {
            this.frame = frame;
            Duration = duration;
            arrive = [];
            depart = [];
        }

        /// <summary>The run's length, in seconds.</summary>
        internal double Duration { get; }

        /// <summary>The frame at <paramref name="time"/>.</summary>
        internal CameraState At(double time) => frame(time);

        /// <summary>When the camera reaches point <paramref name="point"/>.</summary>
        internal double Arrive(int point) => arrive[point];

        /// <summary>When the camera leaves point <paramref name="point"/>: the end of its hold, or its arrival.</summary>
        internal double Depart(int point) => depart[point];

        /// <summary>The run as a clock: each call plays the frame at the time reached and moves on by the step given, the last frame played at the end, then null.</summary>
        internal Func<float, Played?> Clock()
        {
            var time = 0.0;
            var ended = false;
            return dt =>
            {
                if (ended)
                    return null;
                var at = Math.Min(time, Duration);
                ended = at >= Duration;
                time += dt;
                return new Played(At(at), at);
            };
        }

        /// <summary>Every step in the facing, as the distance between unit directions.</summary>
        internal List<Step> FacingSteps() => Steps(t => At(t).Forward, Vector3.Distance, FacingStepFloor, Duration);

        /// <summary>Every step in the position, in yalms.</summary>
        internal List<Step> PositionSteps() =>
            Steps(t => At(t).Position, Vector3.Distance, PositionStepFloor, Duration);

        /// <summary>Every step in the field of view, in radians.</summary>
        internal List<Step> FovSteps() => Steps(t => At(t).Fov, (a, b) => MathF.Abs(a - b), FovStepFloor, Duration);

        /// <summary>Every step in the picture's up, as the distance between unit ups, however far the facing turns with it.</summary>
        internal List<Step> UpSteps() => Steps(t => At(t).Up, Vector3.Distance, UpStepFloor, Duration);

        /// <summary>Every step in the picture's up, leaving out any where the facing turns at least a tenth as far (<see cref="SpinPerTurn"/>) in the same moment: keeping level as the view whips round, as it does where a path doubles back.</summary>
        internal List<Step> PictureSteps()
        {
            var moment = StepWindow / (1 << StepHalvings);
            return
            [
                .. UpSteps()
                    .Where(step =>
                    {
                        var (a, b) = (At(step.Time), At(step.Time + moment));
                        var turn = Vector3.Distance(a.Forward, b.Forward);
                        return !(step.Size <= turn * SpinPerTurn);
                    }),
            ];
        }

        /// <summary>Every snap in the facing, position, field of view and picture, described with its size and time.</summary>
        internal List<string> Snaps() =>
            [
                .. FacingSteps().Select(s => $"facing turns {DistanceDegrees(s.Size):0.###}° at {s.Time:0.####} s"),
                .. PositionSteps().Select(s => $"position jumps {s.Size:0.###} yalms at {s.Time:0.####} s"),
                .. FovSteps().Select(s => $"field of view pops {s.Size / Deg:0.###}° at {s.Time:0.####} s"),
                .. PictureSteps().Select(s => $"picture turns {DistanceDegrees(s.Size):0.###}° at {s.Time:0.####} s"),
            ];

        /// <summary>The most the picture turns about its own centre between 60 fps frames beyond <see cref="SpinPerTurn"/> times the facing's own turn, in radians: a whip, where the picture turns though the view barely does. Not a number once any frame isn't.</summary>
        internal float LargestTwist() =>
            LargestChange(
                Every(FrameSeconds, 0.0, Duration),
                At,
                (a, b) =>
                    Twist(a.Forward, a.Up, b.Forward, b.Up) - (SpinPerTurn * Vectors.AngleBetween(a.Forward, b.Forward))
            );

        /// <summary>The largest turn of the facing between samples <paramref name="step"/> seconds apart from <paramref name="from"/> to <paramref name="to"/>, as the distance between unit directions, 2·sin(θ/2).</summary>
        internal float LargestTurn(double step, double from, double to) =>
            LargestChange(Every(step, from, to), t => At(t).Forward, Vector3.Distance);

        /// <summary>The largest turn of the facing between samples <paramref name="step"/> seconds apart over the whole run, as the distance between unit directions.</summary>
        internal float LargestTurn(double step) => LargestTurn(step, 0.0, Duration);

        /// <summary>The camera's rotation at <paramref name="time"/>, from its facing and up.</summary>
        internal Quaternion Rotation(double time)
        {
            var at = At(time);
            return CameraRotation.FromBasis(at.Forward, at.Up);
        }

        /// <summary>The camera's world turn rate at <paramref name="time"/> (<see cref="TrackRuns.WorldTurnRate"/>), over 2·<paramref name="h"/> seconds.</summary>
        internal Vector3 WorldTurnRate(double time, double h) => TrackRuns.WorldTurnRate(Rotation, time, h);

        /// <summary>The camera's speed at <paramref name="time"/>, in yalms a second: the straight distance from <paramref name="time"/> − <paramref name="h"/> to <paramref name="time"/> + <paramref name="h"/> over 2·<paramref name="h"/>.</summary>
        internal float Speed(double time, double h) =>
            Vector3.Distance(At(time - h).Position, At(time + h).Position) / (float)(2.0 * h);

        /// <summary>How far the horizon leans at <paramref name="time"/>, in radians: the picture's right vector's angle from level.</summary>
        internal float HorizonTilt(double time)
        {
            var at = At(time);
            var right = Vector3.Normalize(Vector3.Cross(at.Forward, at.Up));
            return MathF.Asin(MathF.Min(MathF.Abs(right.Y), 1f));
        }

        /// <summary>How far off <paramref name="point"/> the camera faces at <paramref name="time"/>, in radians: the angle between the facing and the direction to it.</summary>
        internal float Centring(double time, Vector3 point)
        {
            var at = At(time);
            return Vectors.AngleBetween(at.Forward, point - at.Position);
        }
    }

    /// <summary>The angle, in degrees, between unit directions <paramref name="distance"/> apart.</summary>
    private static float DistanceDegrees(float distance) => 2f * MathF.Asin(MathF.Min(distance / 2f, 1f)) / Deg;

    /// <summary>Times <paramref name="step"/> apart from <paramref name="from"/> while short of <paramref name="to"/>, then <paramref name="to"/> itself.</summary>
    internal static IEnumerable<double> Every(double step, double from, double to)
    {
        for (var t = from; t < to; t += step)
            yield return t;
        yield return to;
    }

    /// <summary>Fails at the first frame that isn't well-formed of those <paramref name="advance"/> plays, moving on by each of <paramref name="steps"/> in turn, until it returns null or <paramref name="budget"/> frames have played.</summary>
    internal static void AssertEveryFrameWellFormed(float[] steps, int budget, Func<float, Played?> advance)
    {
        for (var i = 0; i < budget; i++)
        {
            if (advance(steps[i % steps.Length]) is not { } played)
                return;
            if (played.Frame is { } frame)
                AssertWellFormed(frame, $"Frame {i} at {played.Time:0.######} s");
        }
    }

    /// <summary>The largest <paramref name="change"/> between <paramref name="sample"/>s at consecutive <paramref name="times"/>, taken in order; not a number once any change isn't.</summary>
    internal static float LargestChange<T>(IEnumerable<double> times, Func<double, T> sample, Func<T, T, float> change)
    {
        var largest = 0f;
        var started = false;
        T last = default!;
        foreach (var time in times)
        {
            var next = sample(time);
            if (started)
                largest = MathF.Max(largest, change(last, next));
            (last, started) = (next, true);
        }

        return largest;
    }

    /// <summary>The largest <paramref name="value"/> at <paramref name="times"/>, taken in order; not a number once any value isn't.</summary>
    internal static float Largest(IEnumerable<double> times, Func<double, float> value) =>
        times.Aggregate(float.NegativeInfinity, (largest, time) => MathF.Max(largest, value(time)));

    /// <summary>Every step in <paramref name="sample"/> over its first <paramref name="duration"/> seconds, steps within 10 ms of each other counting once at the larger size.</summary>
    private static List<Step> Steps<T>(Func<double, T> sample, Func<T, T, float> distance, float floor, double duration)
    {
        // A smooth change shrinks with the interval it's measured over; a step doesn't. Wherever the channel changes more
        // than its floor in 10 ms, halve the interval 8 times, keeping the half that changes more: a smooth change falls to
        // about 1/256 of what it was, a step stays whole. Anything above a quarter, or not a number, is a step.
        var steps = new List<Step>();
        var from = sample(0.0);
        for (var i = 0; i * StepWindow < duration; i++)
        {
            var (a, b) = (i * StepWindow, Math.Min((i + 1) * StepWindow, duration));
            var to = sample(b);
            var change = distance(from, to);
            if (!(change < floor))
            {
                var (sa, sb) = (from, to);
                for (var h = 0; h < StepHalvings; h++)
                {
                    var m = (a + b) / 2;
                    var sm = sample(m);
                    (a, b, sa, sb) = distance(sa, sm) >= distance(sm, sb) ? (a, m, sa, sm) : (m, b, sm, sb);
                }

                var left = distance(sa, sb);
                if (!(left <= change / 4))
                {
                    if (steps.Count > 0 && a - steps[^1].Time <= StepWindow)
                        steps[^1] = steps[^1] with { Size = MathF.Max(steps[^1].Size, left) };
                    else
                        steps.Add(new Step(a, left));
                }
            }

            from = to;
        }

        return steps;
    }

    /// <summary>The unsigned angle, in radians, the picture turns about its own centre between two frames: <paramref name="upA"/> carried square to <paramref name="forwardB"/> by the minimal rotation from <paramref name="forwardA"/>, against <paramref name="upB"/>.</summary>
    internal static float Twist(Vector3 forwardA, Vector3 upA, Vector3 forwardB, Vector3 upB)
    {
        var carried = Vector3.Transform(upA, CameraRotation.MinimalRotation(forwardA, forwardB));
        return Vectors.AngleBetween(carried, upB);
    }

    /// <summary>The world angular velocity (a rotation vector: axis times radians per second) of <paramref name="rotation"/> at <paramref name="t"/>, by central difference over 2·<paramref name="h"/> seconds: q(t+h)·q(t−h)⁻¹, sign-fixed to W ≥ 0, as axis × angle / (2h).</summary>
    internal static Vector3 WorldTurnRate(Func<double, Quaternion> rotation, double t, double h)
    {
        var relative = Quaternion.Concatenate(Quaternion.Inverse(rotation(t - h)), rotation(t + h));
        if (relative.W < 0f)
            relative = Quaternion.Negate(relative);
        var axis = new Vector3(relative.X, relative.Y, relative.Z);
        var sine = axis.Length();
        var angle = 2f * MathF.Atan2(sine, relative.W);
        var vector = sine < 1e-9f ? axis * 2f : axis / sine * angle;
        return vector / (2f * (float)h);
    }
}
