using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;

namespace Vista.Tests.Regression;

/// <summary>The camera regression scene: one track per camera case, played in order, and its committed file.</summary>
internal static class RegressionScene
{
    /// <summary>The scene file's name, which is also the scene's name in Vista's scene list.</summary>
    internal const string FileName = "Vista - Camera Regression.json";

    /// <summary>The environment variable that makes the file test rewrite the file instead of checking it.</summary>
    internal const string WriteVariable = "VISTA_WRITE_REGRESSION_SCENE";

    /// <summary>30 yalms above the demo scene's anchor, (-94.725105, 19.562155, -10.473951), over Limsa Lominsa Lower Decks.</summary>
    private static readonly Vector3 SceneAnchor = new(-94.725105f, 49.562155f, -10.473951f);

    /// <summary>Yalms between neighbouring track anchors in the grid.</summary>
    private const float GridSpacing = 30f;

    /// <summary>Track anchors in each row of the grid.</summary>
    private const int GridColumns = 4;

    /// <summary>The field of view of every point whose case doesn't vary it, as the demo scene's points have.</summary>
    private const float Fov = 0.78f;

    /// <summary>The cases in playlist order.</summary>
    internal static IReadOnlyList<RegressionCase> Cases { get; } =
    [
        new("Gentle curve: smooth throughout", Travel([P(-10f, 0f, 0f), P(0f, 1f, -3f), P(10f, 0f, 0f)]), 0),
        new(
            "Hold at a sharp turn, look ahead 0: still through the hold",
            TrackEditing.SetHold(Travel(RunIntoASharpTurn, lookAhead: 0f), 4, 2f),
            0
        ),
        new("Crane shot, look ahead 0: looks straight up, no spin", Travel(Crane, lookAhead: 0f), 0),
        new("Crane shot, look ahead 0.5: looks straight up, no spin", Travel(Crane), 0),
        new(
            "Climb drifting across straight up: no flip, no spin",
            Travel([P(0f, -10f, 0f), P(0.4f, 0f, 0f), P(0f, 10f, 0f)]),
            0
        ),
        new(
            "Sharp turn in a vertical plane: no flip, no spin",
            Travel([P(8f, 5f, 0f), P(-7f, 0f, 0f), P(8f, -6f, 0f)]),
            0
        ),
        new("Hold partway up a climb: still through the hold", TrackEditing.SetHold(Travel(Crane), 2, 2f), 0),
        new("Hairpin: turns smoothly", Travel([P(7f, 0f, -2.5f), P(-8f, 0f, 0f), P(7f, 0f, 2.5f)]), 0),
        new(
            "Into and out of a hold: smooth",
            TrackEditing.SetHold(Travel([P(-10f, 0f, 5f), P(0f, 0f, -5f), P(10f, 0f, 5f)]), 1, 2f),
            0
        ),
        new("Easing into the last point: settles without a step", EasingIntoTheEnd(), 0),
        new("Recorded aim with field of view and roll: no pop on arrival", RecordedAim(), 0),
        new("Straight doubleback, look ahead 0.5: snaps round once", Travel(Doubleback), 1),
        new("Straight doubleback, look ahead 0: snaps round once", Travel(Doubleback, lookAhead: 0f), 1),
        new("Vertical loop: upside down over the top, smooth", Travel(Loop), 0),
        new("Recorded aim over the top: straight up, no swing", OverTheTop(), 0),
        new("Look At straight overhead: turns upright, no flip", PassingUnder(), 0),
        new("Climbing turn: horizon stays level", Travel(ClimbingTurn), 0),
        new("Lap back to the start, look ahead 2: no flip as it sets off", LapBackToTheStart(), 0),
        new("Hairpin crossing its own path, look ahead 1.74: one expected flip", HairpinCrossing(), 1),
        new("Field of view recorded at 3° and 143°: zooms from 5° to 120° and back, no pop", PastTheFovRange(), 0),
    ];

    /// <summary>In along +x, up and over a loop 16 yalms high, and out along +x again, each point at least a yalm from the last.</summary>
    private static ControlPoint[] Loop =>
        [
            P(-20f, 0f, 0f),
            P(-6f, 0f, 0f),
            P(4f, 3f, 0f),
            P(7f, 10f, 0f),
            P(0f, 16f, 0f),
            P(-7f, 10f, 0f),
            P(-4f, 3f, 0f),
            P(6f, 0f, 0f),
            P(20f, 0f, 0f),
        ];

    /// <summary>A quarter turn round a 10-yalm circle at a time, rising 3 yalms each: a steady climbing turn.</summary>
    private static ControlPoint[] ClimbingTurn =>
        [
            .. Enumerable
                .Range(0, 9)
                .Select(i => P(10f * MathF.Cos(i * MathF.PI / 2f), 3f * i, 10f * MathF.Sin(i * MathF.PI / 2f))),
        ];

    /// <summary>Recorded aim from pitch 60° to 120° over a 10-yalm leg: yaw 180°, pitch 60°, roll 180° is pitch 120°, so the shortest turn passes straight up.</summary>
    private static Track OverTheTop()
    {
        var track = TrackEditing.Empty(AimMode.AimKeys);
        track = TrackEditing.Append(track, new ControlPoint(new Vector3(-5f, 0f, 0f), 0f, MathF.PI / 3f, Fov, 0f));
        return TrackEditing.Append(
            track,
            new ControlPoint(new Vector3(5f, 0f, 0f), MathF.PI, MathF.PI / 3f, Fov, MathF.PI)
        );
    }

    /// <summary>Along x under a Look At point 10 yalms up, passing straight beneath it.</summary>
    private static Track PassingUnder()
    {
        var track = TrackEditing.Empty(AimMode.LookAt);
        foreach (var point in new[] { P(-10f, 0f, 0f), P(0f, 0f, 0f), P(20f, 0f, 0f) })
            track = TrackEditing.Append(track, point);
        return TrackEditing.SetLookAt(track, new Vector3(0f, 10f, 0f));
    }

    /// <summary>Four 20-yalm legs along +z, starting 70 yalms out from the grid, then 15 back at 179°: reverting the hold fix, the float wobble 80 yalms along turns a look ahead 0 aim at that turn by about 0.4°.</summary>
    private static ControlPoint[] RunIntoASharpTurn =>
        [P(0f, 0f, -70f), P(0f, 0f, -50f), P(0f, 0f, -30f), P(0f, 0f, -10f), P(0f, 0f, 10f), P(0.26f, 0f, -5f)];

    /// <summary>Level along -z, straight up 20 yalms, then level along +x, in legs of 10.</summary>
    private static ControlPoint[] Crane =>
        [P(0f, -10f, 10f), P(0f, -10f, 0f), P(0f, 0f, 0f), P(0f, 10f, 0f), P(10f, 10f, 0f)];

    /// <summary>Out 15 yalms along +x and straight back 10.</summary>
    private static ControlPoint[] Doubleback => [P(-8f, 0f, 0f), P(7f, 0f, 0f), P(-3f, 0f, 0f)];

    /// <summary>The scene as the builder makes it, with fixed ids so its file only changes when a case does.</summary>
    internal static Scene Build()
    {
        var tracks = Cases
            .Select(
                (c, i) =>
                    c.Track with
                    {
                        Id = Id(1, i),
                        Name = c.Name,
                        Anchor = new Anchor(GridSpot(i), 0f),
                        AnchorPlaced = true,
                    }
            )
            .ToArray();
        var playlist = tracks.Select((t, i) => new PlaylistEntry(Id(2, i), t.Id)).ToArray();
        return new Scene(tracks, new HashSet<Guid>(), playlist, new Anchor(SceneAnchor, 0f), AnchorPlaced: true);
    }

    /// <summary>The committed scene file's path in the repository.</summary>
    internal static string FilePath() => Path.Combine(Fixtures.RepositoryRoot(), "tests", "scenes", FileName);

    /// <summary>A fixed id: the kind (1 a track, 2 a playlist entry) and the case's index.</summary>
    private static Guid Id(int kind, int index) => new($"00000000-0000-0000-{kind:D4}-{index:D12}");

    /// <summary>Case <paramref name="index"/>'s track anchor, local to the scene anchor, in a grid centred on it.</summary>
    private static Vector3 GridSpot(int index)
    {
        var rows = (Cases.Count + GridColumns - 1) / GridColumns;
        var column = index % GridColumns;
        var row = index / GridColumns;
        return new Vector3(
            (column - ((GridColumns - 1) / 2f)) * GridSpacing,
            0f,
            (row - ((rows - 1) / 2f)) * GridSpacing
        );
    }

    /// <summary>A point at a place local to its track anchor, with no recorded aim or roll.</summary>
    private static ControlPoint P(float x, float y, float z) => new(new Vector3(x, y, z), 0f, 0f, Fov);

    /// <summary>A Direction of travel track through <paramref name="points"/> at the default speed.</summary>
    private static Track Travel(ControlPoint[] points, float lookAhead = TrackEditing.DefaultLookAhead) =>
        TrackEditing.SetLookAhead(Fixtures.TrackThrough(points, AimMode.PathTangent), lookAhead);

    /// <summary>A gentle curve whose last leg eases out into a hold at the end.</summary>
    private static Track EasingIntoTheEnd()
    {
        var track = Travel([P(-10f, 0f, 0f), P(0f, 0f, -4f), P(10f, 0f, -2f)]);
        return TrackEditing.SetHold(LegEasing.Set(track, 2, Easing.EaseOut), 2, 1.5f);
    }

    /// <summary>A 10-yalm triangle at 25 yalms a second, holding 2 s back at its start: at 0 s the spot 2 s ahead waits where the camera is.</summary>
    private static Track LapBackToTheStart()
    {
        var track = Travel(
            [P(-5f, 0f, 5f), P(-5f, 0f, -5f), P(5f, 0f, -5f), P(-5f, 0f, 5f)],
            TrackEditing.MaxLookAhead
        );
        return TrackEditing.SetHold(TrackEditing.SetSpeed(track, 25f), 3, 2f);
    }

    /// <summary>A teardrop at 15 yalms a second whose way back crosses its way out, with a look ahead of the 1.74 s the loop takes from the crossing back to it: the spot ahead passes through the camera there, and the aim turns round at once.</summary>
    private static Track HairpinCrossing() =>
        TrackEditing.SetSpeed(
            Travel([P(-10f, 0f, -3f), P(5f, 0f, 3f), P(5f, 0f, -3f), P(-10f, 0f, 3f)], 1.7412066f),
            15f
        );

    /// <summary>Recorded aim through points recorded at 3°, 143° and 3°, past the editor's 5° to 120°, holding a second at each.</summary>
    private static Track PastTheFovRange()
    {
        var track = TrackEditing.Empty(AimMode.AimKeys);
        foreach (var (x, fov) in new[] { (-10f, 3f), (0f, 143f), (10f, 3f) })
            track = TrackEditing.Append(track, new ControlPoint(new Vector3(x, 0f, 0f), 0f, 0f, fov * Fixtures.Deg));
        for (var i = 0; i < track.Points.Count; i++)
            track = TrackEditing.SetHold(track, i, 1f);
        return track;
    }

    /// <summary>Recorded aim through points that each look, zoom and roll differently, holding at the middle and the end.</summary>
    private static Track RecordedAim()
    {
        var track = TrackEditing.Empty(AimMode.AimKeys);
        track = TrackEditing.Append(track, new ControlPoint(new Vector3(-10f, 0f, 0f), -1.2f, -0.1f, 0.7f, 0f));
        track = TrackEditing.Append(track, new ControlPoint(new Vector3(0f, 1f, -3f), -1.9f, 0.1f, 1.1f, 0.25f));
        track = TrackEditing.Append(track, new ControlPoint(new Vector3(10f, 0f, 0f), -1.4f, 0f, 0.6f, -0.15f));
        return TrackEditing.SetHold(TrackEditing.SetHold(track, 1, 2f), 2, 1.5f);
    }
}
