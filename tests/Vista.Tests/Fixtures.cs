using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Xunit;

namespace Vista.Tests;

/// <summary>Control points, tracks and assertions shared across the test suite.</summary>
internal static class Fixtures
{
    /// <summary>Radians in a degree.</summary>
    internal const float Deg = MathF.PI / 180f;

    /// <summary>The fewest seconds Direction of travel looks ahead in a generated track, since with none it turns at once where the path doubles back, by design.</summary>
    private const float MinGeneratedLookAhead = 0.1f;

    /// <summary>Where a generated Look At track looks: off to the side of every generated point, so the camera never passes under it and its aim never goes vertical.</summary>
    private static readonly Vector3 GeneratedLookAt = new(200f, 0f, 0f);

    /// <summary>Any place within 30 yalms across and 5 up or down.</summary>
    internal static readonly Gen<Vector3> AnyPosition = Gen.Select(
        Gen.Float[-30f, 30f],
        Gen.Float[-5f, 5f],
        Gen.Float[-30f, 30f],
        (x, y, z) => new Vector3(x, y, z)
    );

    /// <summary>A control point anywhere in <see cref="AnyPosition"/>, with any yaw, pitch within 1 radian either way, roll within 0.5 and any field of view the editor allows.</summary>
    private static readonly Gen<ControlPoint> AnyPoint = Gen.Select(
        AnyPosition,
        Gen.Float[-MathF.PI, MathF.PI],
        Gen.Float[-1f, 1f],
        Gen.Float[EditLimits.MinFov, EditLimits.MaxFov],
        Gen.Float[-0.5f, 0.5f],
        (position, yaw, pitch, fov, roll) => new ControlPoint(position, yaw, pitch, fov, roll)
    );

    /// <summary>The sharpest turn from one leg into the next in a generated track: Direction of travel snaps round where a path runs back along itself, by design, and within a degree of that it whips round in milliseconds, where float noise reads as a step (a 179.48° turn did).</summary>
    private const float SharpestGeneratedTurn = 179f * Deg;

    /// <summary>Two to six points, each at least a yalm from the one before, and no leg turning back on the one before sharper than <see cref="SharpestGeneratedTurn"/>.</summary>
    private static readonly Gen<ControlPoint[]> AnyPoints = AnyPoint
        .Array[2, 6]
        .Where(ps => ps.Zip(ps.Skip(1)).All(p => Vector3.Distance(p.First.Position, p.Second.Position) >= 1f))
        .Where(ps =>
            Enumerable
                .Range(2, ps.Length - 2)
                .All(i =>
                    Vector3.Dot(
                        Vector3.Normalize(ps[i - 1].Position - ps[i - 2].Position),
                        Vector3.Normalize(ps[i].Position - ps[i - 1].Position)
                    ) >= MathF.Cos(SharpestGeneratedTurn)
                )
        );

    /// <summary>A track through <see cref="AnyPoints"/> aimed along its path, by its aim keys or at <see cref="GeneratedLookAt"/>, with random speed, holds, leg times and look ahead, built as the editor builds it.</summary>
    internal static readonly Gen<Track> AnyPathTrack =
        from points in AnyPoints
        from aim in Gen.OneOfConst(AimMode.PathTangent, AimMode.AimKeys, AimMode.LookAt)
        from speed in Gen.Float[2f, 20f]
        from lookAhead in Gen.Float[aim == AimMode.PathTangent ? MinGeneratedLookAhead : 0f, TrackEditing.MaxLookAhead]
        // Per point: 0 nothing, 1 a hold, 2 a timed leg into it, 3 both; then the hold and the leg's seconds.
        from timing in Gen.Select(Gen.Int[0, 3], Gen.Float[0f, 3f], Gen.Float[0.5f, 10f]).Array[points.Length]
        select PathTrack(points, aim, speed, lookAhead, timing);

    private static Track PathTrack(
        ControlPoint[] points,
        AimMode aim,
        float speed,
        float lookAhead,
        (int Kind, float Hold, float Leg)[] timing
    )
    {
        var track = TrackEditing.SetSpeed(TrackEditing.Empty(aim), speed);
        foreach (var point in points)
            track = TrackEditing.Append(track, point);
        for (var i = 0; i < points.Length; i++)
        {
            if ((timing[i].Kind & 1) != 0)
                track = TrackEditing.SetHold(track, i, timing[i].Hold);
            if ((timing[i].Kind & 2) != 0 && i > 0)
                track = TrackEditing.SetLegDuration(track, i, timing[i].Leg);
        }

        track = TrackEditing.SetLookAhead(track, lookAhead);
        return aim == AimMode.LookAt ? TrackEditing.SetLookAt(track, GeneratedLookAt) : track;
    }

    /// <summary>A control point at the given position, aim and field of view.</summary>
    internal static ControlPoint Point(
        float x,
        float y = 0f,
        float z = 0f,
        float yaw = 0f,
        float pitch = 0f,
        float fov = 1f,
        float roll = 0f
    ) => new(new Vector3(x, y, z), yaw, pitch, fov, roll);

    /// <summary>The demo scene the plugin ships, as the test project copies it.</summary>
    internal static string DemoSceneJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Demo", "Demo - Limsa.json"));

    /// <summary>The shipped demo scene, read.</summary>
    internal static Scene DemoScene() => SceneJson.Read(DemoSceneJson());

    // Points at 0,10,20 at 2 yalms per second: keys at times 0, 5, 10.
    internal static Track Build3PointTrack()
    {
        var track = TrackEditing.Empty() with { Speed = 2f };
        track = TrackEditing.Append(track, Point(0f, 0f, 0f));
        track = TrackEditing.Append(track, Point(10f, 0f, 0f));
        track = TrackEditing.Append(track, Point(20f, 0f, 0f));
        return track;
    }

    // Points at x = 0, 5, 10 in two 5 s legs: a 10 s track.
    internal static Track StraightTrack(bool loop = false, PlaybackDirection direction = PlaybackDirection.Forward)
    {
        var track = TrackEditing.SetDirection(
            TrackEditing.SetLoop(TrackEditing.Empty(AimMode.PathTangent), loop),
            direction
        );
        foreach (var x in new[] { 0f, 5f, 10f })
            track = TrackEditing.Append(track, Point(x));
        return TrackEditing.SetLegDuration(TrackEditing.SetLegDuration(track, 1, 5f), 2, 5f);
    }

    /// <summary>Asserts the frame looks toward <paramref name="target"/>, comparing unit directions to <paramref name="precision"/> decimal places.</summary>
    internal static void AimsAt(Vector3 target, CameraState frame, int precision)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = Vector3.Normalize(frame.LookAt - frame.Position);
        Assert.Equal(want.X, got.X, precision);
        Assert.Equal(want.Y, got.Y, precision);
        Assert.Equal(want.Z, got.Z, precision);
    }

    /// <summary>Seconds a channel's change is first measured over when looking for steps.</summary>
    private const double StepWindow = 0.01;

    /// <summary>Halvings of a window that changes more than its floor, each keeping the half that changes more.</summary>
    private const int StepHalvings = 8;

    /// <summary>The facing's step floor, 0.3°, as the distance between unit directions, 2·sin(θ/2), which is θ to within 1e-7 here.</summary>
    internal const float FacingStepFloor = 0.3f * Deg;

    /// <summary>The position's step floor, in yalms.</summary>
    internal const float PositionStepFloor = 0.05f;

    /// <summary>The field of view's step floor, 0.1° in radians.</summary>
    internal const float FovStepFloor = 0.1f * Deg;

    /// <summary>The smallest change in the picture's up, as the distance between unit ups, looked at for a step: 0.1°.</summary>
    internal const float UpStepFloor = 0.1f * Deg;

    /// <summary>A sudden change in a channel: when it happens and how far it jumps.</summary>
    internal readonly record struct Step(double Time, float Size);

    /// <summary>Every step in <paramref name="sample"/> over its first <paramref name="duration"/> seconds, steps within 10 ms of each other counting once at the larger size.</summary>
    internal static List<Step> Steps<T>(
        Func<double, T> sample,
        Func<T, T, float> distance,
        float floor,
        double duration
    )
    {
        // A smooth change shrinks with the interval it's measured over; a step doesn't. Wherever the channel changes more
        // than its floor in 10 ms, halve the interval 8 times, keeping the half that changes more: a smooth change falls to
        // about 1/256 of what it was, a step stays whole. Anything above a quarter is a step.
        var steps = new List<Step>();
        var from = sample(0.0);
        for (var i = 0; i * StepWindow < duration; i++)
        {
            var (a, b) = (i * StepWindow, Math.Min((i + 1) * StepWindow, duration));
            var to = sample(b);
            var change = distance(from, to);
            if (change >= floor)
            {
                var (sa, sb) = (from, to);
                for (var h = 0; h < StepHalvings; h++)
                {
                    var m = (a + b) / 2;
                    var sm = sample(m);
                    (a, b, sa, sb) = distance(sa, sm) >= distance(sm, sb) ? (a, m, sa, sm) : (m, b, sm, sb);
                }

                var left = distance(sa, sb);
                if (left > change / 4)
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

    /// <summary>The most the picture may turn about its own centre from one 60 fps frame to the next: 360° a second.</summary>
    internal const float PictureSpinLimit = 6f * Deg;

    /// <summary>The most the picture turns about its own centre between 60 fps frames over <paramref name="duration"/> seconds, in radians, leaving out frames where the facing snaps round more than a quarter turn, which the step checks judge.</summary>
    internal static float LargestTwist(Func<double, CameraState> frame, double duration)
    {
        const double frameSeconds = 1.0 / 60.0;
        var largest = 0f;
        var last = frame(0.0);
        for (var t = frameSeconds; t <= duration; t += frameSeconds)
        {
            var next = frame(t);
            var (from, to) = (last.LookAt - last.Position, next.LookAt - next.Position);
            if (Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to)) > 0f)
                largest = MathF.Max(largest, CameraRotation.Twist(from, last.Up, to, next.Up));
            last = next;
        }

        return largest;
    }

    /// <summary>Asserts two vectors agree on every component within the given tolerance.</summary>
    internal static void Near(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }
}
