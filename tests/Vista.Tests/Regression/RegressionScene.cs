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

    /// <summary>The field of view of every point whose case doesn't vary it.</summary>
    private const float Fov = PathShapes.Fov;

    /// <summary>The cases in playlist order.</summary>
    internal static IReadOnlyList<RegressionCase> Cases { get; } =
    [
        new("Gentle curve: smooth throughout", Travel([P(-10f, 0f, 0f), P(0f, 1f, -3f), P(10f, 0f, 0f)]), 0),
        new(
            "Hold at a sharp turn, look ahead 0: still through the hold",
            TrackEditing.SetHold(Travel(RunIntoASharpTurn, lookAhead: 0f), 4, 2f),
            0
        ),
        new("Crane shot, look ahead 0: looks straight up, no spin", Travel(Placed(PathShapes.Crane), lookAhead: 0f), 0),
        new("Crane shot, look ahead 2.5: looks straight up, no spin", Travel(Placed(PathShapes.Crane)), 0),
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
        new(
            "Hold partway up a climb: still through the hold",
            TrackEditing.SetHold(Travel(Placed(PathShapes.Crane)), 2, 2f),
            0
        ),
        new("Hairpin: turns smoothly", Travel([P(7f, 0f, -2.5f), P(-8f, 0f, 0f), P(7f, 0f, 2.5f)]), 0),
        new(
            "Into and out of a hold: smooth",
            TrackEditing.SetHold(Travel([P(-10f, 0f, 5f), P(0f, 0f, -5f), P(10f, 0f, 5f)]), 1, 2f),
            0
        ),
        new("Easing into the last point: settles without a step", EasingIntoTheEnd(), 0),
        new("Recorded aim with field of view and roll: no pop on arrival", RecordedAim(), 0),
        new("Straight doubleback, look ahead 2.5: snaps round once", Travel(Placed(PathShapes.Doubleback)), 1),
        new(
            "Straight doubleback, look ahead 0: snaps round once",
            Travel(Placed(PathShapes.Doubleback), lookAhead: 0f),
            1
        ),
        new("Vertical loop: upside down over the top, smooth", Travel(Placed(PathShapes.Loop)), 0),
        new("Recorded aim over the top: straight up, no swing", OverTheTop(), 0),
        new("Recorded aim up and over through a middle point: one steady turn", RecordedAimUpAndOverAMiddlePoint(), 0),
        new("Look At straight overhead: turns round from point to point, no flip", PassingUnder(), 0),
        new("Climbing turn: horizon stays level", Travel(Placed(PathShapes.ClimbingTurn)), 0),
        new("Uneven spacing: steady speed through its points", Travel(Placed(PathShapes.UnevenSpacing)), 0),
        new("Lap back to the start, look ahead 10: no flip as it sets off", LapBackToTheStart(), 0),
        new("Hairpin crossing its own path, look ahead 8.47: one expected flip", HairpinCrossing(), 1),
        new("Field of view recorded at 3° and 143°: zooms from 5° to 120° and back, no pop", PastTheFovRange(), 0),
        new(
            "Direction of travel through a held corner, look ahead 5: holds its look, no swing",
            TrackEditing.SetHold(Travel([P(0f, 0f, 0f), P(10f, 0f, 0f), P(10f, 0f, 10f)], lookAhead: 5f), 1, 3f),
            0
        ),
        new(
            "Lap held at its start, look ahead 10: faces the way the lap finishes, no turn-round as it sets off",
            TrackEditing.SetHold(LapBackToTheStart(), 0, 2f),
            0
        ),
        new(
            "Held where the path ends, look ahead 10: faces the way the lap finishes, no turn-round as it sets off",
            HeldWhereThePathEnds(),
            0
        ),
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

    /// <summary>Recorded aim east 45° up, straight up with the picture's top to the north, west 45° up, 2 s a leg: the shot that found the turn-rate jump at a middle point.</summary>
    private static Track RecordedAimUpAndOverAMiddlePoint()
    {
        (float Yaw, float Pitch)[] looks =
        [
            (-MathF.PI / 2f, MathF.PI / 4f),
            (MathF.PI, MathF.PI / 2f),
            (MathF.PI / 2f, MathF.PI / 4f),
        ];
        var track = TrackEditing.Empty(AimMode.AimKeys);
        foreach (var (position, (yaw, pitch)) in PathShapes.UpAndOver.Zip(looks))
            track = TrackEditing.Append(track, new ControlPoint(position, yaw, pitch, Fov));
        track = TrackEditing.SetLegDuration(track, 1, 2f);
        return TrackEditing.SetLegDuration(track, 2, 2f);
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
        var entries = tracks.Select((t, i) => new PlaylistEntry(Id(2, i), t.Id)).ToArray();
        var playlist = new Playlist(Id(3, 0), PlaylistEditing.FirstName, entries);
        return new Scene(
            tracks,
            new HashSet<Guid>(),
            [playlist],
            playlist.Id,
            new Anchor(SceneAnchor, 0f),
            AnchorPlaced: true
        );
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

    /// <summary>Points at <paramref name="positions"/>, local to their track anchor, with no recorded aim or roll.</summary>
    private static ControlPoint[] Placed(Vector3[] positions) =>
        [.. positions.Select(p => new ControlPoint(p, 0f, 0f, Fov))];

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

    /// <summary>A 2.5-yalm triangle, about 8.8 yalms round, holding 2 s back at its start: under the 10-yalm look ahead, so at 0 s the spot is past the end, carried on beyond the camera.</summary>
    private static Track LapBackToTheStart()
    {
        var track = Travel(
            [P(-1.25f, 0f, 1.25f), P(-1.25f, 0f, -1.25f), P(1.25f, 0f, -1.25f), P(-1.25f, 0f, 1.25f)],
            TrackEditing.MaxLookAhead
        );
        return TrackEditing.SetHold(track, 3, 2f);
    }

    /// <summary>In along +x to a point held 2 s, then the lap above back to it: the lap is under the 10-yalm look ahead, so while the camera holds the spot is past the end, beyond it.</summary>
    private static Track HeldWhereThePathEnds()
    {
        var track = Travel(
            [
                P(-11.25f, 0f, 1.25f),
                P(-1.25f, 0f, 1.25f),
                P(-1.25f, 0f, -1.25f),
                P(1.25f, 0f, -1.25f),
                P(-1.25f, 0f, 1.25f),
            ],
            TrackEditing.MaxLookAhead
        );
        return TrackEditing.SetHold(track, 1, 2f);
    }

    /// <summary>A teardrop whose way back crosses its way out, with a look ahead of the 8.47 yalms round the loop from the crossing back to it, found numerically: the spot ahead passes through the camera there, and the aim turns round at once.</summary>
    private static Track HairpinCrossing() =>
        Travel([P(-10f / 3f, 0f, -1f), P(5f / 3f, 0f, 1f), P(5f / 3f, 0f, -1f), P(-10f / 3f, 0f, 1f)], 8.465311f);

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
