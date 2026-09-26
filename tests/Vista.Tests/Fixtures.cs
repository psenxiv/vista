using System.Numerics;
using System.Runtime.CompilerServices;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;

namespace Vista.Tests;

/// <summary>Control points, tracks and assertions shared across the test suite.</summary>
internal static class Fixtures
{
    /// <summary>The repository's root folder, found from the test run's own folder.</summary>
    internal static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(folder.FullName, "Vista.sln")))
            folder = folder.Parent ?? throw new DirectoryNotFoundException("The test run isn't inside the repository.");
        return folder.FullName;
    }

    /// <summary>Where each failing property's input is written, one file per property. Gitignored, and emptied at the start of every property run by scripts/verify.sh and scripts/soak.sh.</summary>
    internal static string CounterexampleFolder =>
        Path.Combine(RepositoryRoot(), "tests", "Vista.Tests", "obj", "counterexamples");

    /// <summary><paramref name="print"/>, also writing what it prints to <see cref="CounterexampleFolder"/> under the calling property's name, so a failing input is kept until it's made an example test.</summary>
    internal static Func<T, string> Kept<T>(Func<T, string> print, [CallerMemberName] string property = "") =>
        input =>
        {
            var text = print(input);
            Directory.CreateDirectory(CounterexampleFolder);
            File.WriteAllText(Path.Combine(CounterexampleFolder, $"{property}.txt"), text);
            return text;
        };

    /// <summary>Radians in a degree: <see cref="Angles.Degree"/>, short for the many tests that turn by degrees.</summary>
    internal const float Deg = Angles.Degree;

    /// <summary>A quarter turn in radians.</summary>
    internal const float QuarterTurn = MathF.PI / 2f;

    /// <summary>The fewest seconds Direction of travel looks ahead in a generated track, since with none it turns at once where the path doubles back, by design.</summary>
    private const float MinGeneratedLookAhead = 0.1f;

    /// <summary>Where a generated Look At track looks: above the middle of the generated points, so cameras pass under it and look steeply up.</summary>
    private static readonly Vector3 GeneratedLookAt = new(0f, 15f, 0f);

    /// <summary>Any place within 30 yalms across and 5 up or down.</summary>
    internal static readonly Gen<Vector3> AnyPosition = Gen.Select(
        Gen.Float[-30f, 30f],
        Gen.Float[-5f, 5f],
        Gen.Float[-30f, 30f],
        (x, y, z) => new Vector3(x, y, z)
    );

    /// <summary>A control point anywhere in <see cref="AnyPosition"/>, facing any way and rolled any way, with any field of view the editor allows.</summary>
    internal static readonly Gen<ControlPoint> AnyPoint = Gen.Select(
        AnyPosition,
        Gen.Float[-MathF.PI, MathF.PI],
        Gen.Float[-MathF.PI / 2f, MathF.PI / 2f],
        // CsCheck's Float[start, finish] can land a few float steps below start, so the FoV is clamped into the editor's range.
        Gen.Float[EditLimits.MinFov, EditLimits.MaxFov]
            .Select(fov => Math.Clamp(fov, EditLimits.MinFov, EditLimits.MaxFov)),
        Gen.Float[-MathF.PI, MathF.PI],
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

    /// <summary>A track through <see cref="AnyPoints"/> aimed along its path, by its aim keys or at <see cref="GeneratedLookAt"/>, with random speed, holds, leg times and look ahead, built as the editor builds it; a Direction of travel track whose look-ahead spot passes through the moving camera (<see cref="SpotPassesThroughCamera"/>) is left out.</summary>
    internal static readonly Gen<Track> AnyPathTrack = (
        from points in AnyPoints
        from aim in Gen.OneOfConst(AimMode.PathTangent, AimMode.AimKeys, AimMode.LookAt)
        from speed in Gen.Float[2f, 20f]
        from lookAhead in Gen.Float[aim == AimMode.PathTangent ? MinGeneratedLookAhead : 0f, TrackEditing.MaxLookAhead]
        // Per point: 0 nothing, 1 a hold, 2 a timed leg into it, 3 both; then the hold and the leg's seconds.
        from timing in Gen.Select(Gen.Int[0, 3], Gen.Float[0f, 3f], Gen.Float[0.5f, 10f]).Array[points.Length]
        select PathTrack(points, aim, speed, lookAhead, timing)
    ).Where(track => !SpotPassesThroughCamera(track));

    /// <summary>How close, in yalms, a Direction of travel camera may come to its look-ahead spot mid-shot in a generated track: nearer, the spot passes through it where a hairpin's sides cross within the look ahead, and the aim turns round at once, by design.</summary>
    private const float ClosestGeneratedSpot = 0.05f;

    /// <summary>Seconds between the times the camera and its look-ahead spot are compared.</summary>
    private const double SpotStep = 0.01;

    /// <summary>True when a Direction of travel track's camera comes within <see cref="ClosestGeneratedSpot"/> of its look-ahead spot while both move: neither in a hold, and the spot short of the end, where it waits. A spot waiting for the camera, as at the start of a lap back to where it began, doesn't count; the gap is taken as straight between the times compared.</summary>
    internal static bool SpotPassesThroughCamera(Track track)
    {
        if (track.Aim != AimMode.PathTangent || track.LookAhead <= 0f)
            return false;
        var evaluator = new TrackEvaluator(track with { Aim = AimMode.AimKeys });
        var ahead = track.LookAhead;
        Vector3 Place(double t) => evaluator.Evaluate(t)!.Value.Position;

        Vector3? before = null;
        for (var t = SpotStep; t < evaluator.Duration; t += SpotStep)
        {
            var spot = evaluator.TravelledAhead(t, ahead);
            if (spot >= evaluator.Duration)
                break;
            if (!(evaluator.SlopeAt(t) > 0f && evaluator.SlopeAt(spot) > 0f))
            {
                before = null;
                continue;
            }

            var gap = Place(spot) - Place(t);
            if (before is { } last && ClosestToZero(last, gap) < ClosestGeneratedSpot)
                return true;
            before = gap;
        }

        return false;
    }

    /// <summary>The shortest distance from the origin to the straight line from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float ClosestToZero(Vector3 a, Vector3 b)
    {
        var along = b - a;
        var length = along.LengthSquared();
        var share = length > 0f ? Fraction.Clamp(-Vector3.Dot(a, along) / length) : 0f;
        return (a + (along * share)).Length();
    }

    private static Track PathTrack(
        ControlPoint[] points,
        AimMode aim,
        float speed,
        float lookAhead,
        (int Kind, float Hold, float Leg)[] timing
    )
    {
        var track = TrackThrough(points, aim, speed);
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

    /// <summary>A track as a scene file, so a failing property prints something to paste into a test.</summary>
    internal static string PrintTrack(Track track) => SceneJson.Write(new Scene([track], new HashSet<Guid>(), []));

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

    /// <summary>A track through <paramref name="points"/>, aimed by <paramref name="aim"/>, at <paramref name="speed"/> yalms a second, appended as the editor appends them.</summary>
    internal static Track TrackThrough(
        IEnumerable<ControlPoint> points,
        AimMode aim = AimMode.AimKeys,
        float speed = TrackEditing.DefaultSpeed
    )
    {
        var track = TrackEditing.SetSpeed(TrackEditing.Empty(aim), speed);
        foreach (var point in points)
            track = TrackEditing.Append(track, point);
        return track;
    }

    /// <summary><paramref name="track"/> with points at x = 0 and 10 appended: one 10-yalm leg.</summary>
    internal static Track WithTwoPoints(Track track) =>
        TrackEditing.Append(TrackEditing.Append(track, Point(0f)), Point(10f));

    /// <summary>Goes live with the playlist and plays it from the start: Cue, then Play.</summary>
    internal static void GoLive(SessionState state)
    {
        state.Cue();
        state.Play();
    }

    /// <summary>Editing a new session whose track has points at x = 0 and 10: one 2 s leg at the default speed.</summary>
    internal static SessionState EditingTwoPoints()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(WithTwoPoints);
        return state;
    }

    /// <summary>Live with a playlist of <paramref name="state"/>'s edited track, first given points at x = 0 and 10.</summary>
    internal static SessionState LiveTwoPoints(SessionState? state = null)
    {
        if (state is null)
            state = EditingTwoPoints();
        else
            state.ChangeTrack(WithTwoPoints);
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        return state;
    }

    /// <summary>Guard, the character tests watch and follow, with feet at <paramref name="feet"/>, facing <paramref name="facing"/>.</summary>
    internal static LoadedCharacter Guard(Vector3 feet, float facing = 0f) => new("Guard", null, feet, facing);

    /// <summary>Nearby characters holding only Guard, feet at <paramref name="feet"/>, facing <paramref name="facing"/>.</summary>
    internal static NearbyCharacters GuardStanding(Vector3 feet, float facing = 0f)
    {
        var characters = new NearbyCharacters();
        characters.Update([Guard(feet, facing)]);
        return characters;
    }

    /// <summary>Puts Guard in <paramref name="characters"/> with their aim point, the default aim height above their feet, at <paramref name="aim"/>.</summary>
    internal static void GuardAt(NearbyCharacters characters, Vector3 aim) =>
        characters.Update([Guard(aim - new Vector3(0f, TrackEditing.DefaultAimHeight, 0f))]);

    /// <summary>Puts Guard's aim point at (x, 0, −10), in front of a camera at the origin facing along −z.</summary>
    internal static void GuardAt(NearbyCharacters characters, float x) => GuardAt(characters, new Vector3(x, 0f, -10f));

    /// <summary>Nearby characters holding only Guard, with their aim point at (x, 0, −10).</summary>
    internal static NearbyCharacters GuardAt(float x)
    {
        var characters = new NearbyCharacters();
        GuardAt(characters, x);
        return characters;
    }

    /// <summary>A track of one point at the origin, recorded aim yaw <paramref name="yaw"/>, watching Guard with <paramref name="smoothing"/>, held <paramref name="hold"/> seconds.</summary>
    internal static Track WatchingGuard(float smoothing = 0f, float hold = 0f, float yaw = 0f)
    {
        var track = TrackEditing.Append(TrackEditing.Empty(AimMode.WatchTarget), Point(0f, yaw: yaw));
        return TrackEditing.SetHold(track, 0, hold) with { TargetName = "Guard", Smoothing = smoothing };
    }

    /// <summary>Any aim height and smoothing a track can be set to.</summary>
    internal static readonly Gen<(float AimHeight, float Smoothing)> AnyTargetSettings = Gen.Select(
        Gen.Float[0f, TrackEditing.MaxAimHeight],
        Gen.Float[0f, 1f]
    );

    /// <summary><paramref name="track"/> naming its character and taking <paramref name="settings"/>, set as the editor sets them.</summary>
    internal static Track WithTarget(
        Track track,
        string? name,
        string? world,
        (float AimHeight, float Smoothing) settings
    ) =>
        TrackEditing.SetSmoothing(
            TrackEditing.SetAimHeight(TrackEditing.SetTarget(track, name, world), settings.AimHeight),
            settings.Smoothing
        );

    /// <summary>The rate of change of <paramref name="value"/> at <paramref name="t"/>, from the left and from the right, each over <paramref name="step"/>.</summary>
    internal static (float Left, float Right) Slopes(Func<double, float> value, double t, double step)
    {
        var at = value(t);
        return ((float)((at - value(t - step)) / step), (float)((value(t + step) - at) / step));
    }

    /// <summary>The rate of change of <paramref name="value"/> at <paramref name="t"/>, from the left and from the right, each over <paramref name="step"/>.</summary>
    internal static (Vector3 Left, Vector3 Right) Slopes(Func<double, Vector3> value, double t, double step)
    {
        var at = value(t);
        return ((at - value(t - step)) / (float)step, (value(t + step) - at) / (float)step);
    }

    /// <summary>Asserts the frame looks toward <paramref name="target"/>, comparing unit directions to <paramref name="precision"/> decimal places.</summary>
    internal static void AimsAt(Vector3 target, CameraState frame, int precision)
    {
        var want = Vector3.Normalize(target - frame.Position);
        var got = frame.Forward;
        Assert.Equal(want.X, got.X, precision);
        Assert.Equal(want.Y, got.Y, precision);
        Assert.Equal(want.Z, got.Z, precision);
    }

    /// <summary>Fails, naming <paramref name="where"/>, the first rule broken and the value that broke it, unless the frame is well-formed.</summary>
    internal static void AssertWellFormed(CameraState frame, string where)
    {
        if (WellFormed.FirstBroken(frame) is { } rule)
            Assert.Fail($"{where}: {rule}");
    }

    /// <summary>A frame step in seconds: mostly up to two 60 fps frames, sometimes up to a 2 s hitch.</summary>
    internal static readonly Gen<float> AnyFrameStep = Gen.Frequency(
        (4, Gen.Float[0f, 1f / 30f]),
        (1, Gen.Float[0f, 2f])
    );

    /// <summary>Asserts two vectors agree on every component within the given tolerance.</summary>
    internal static void Near(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }
}
