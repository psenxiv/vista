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

    /// <summary>25 yalms above the demo scene's anchor, (-94.725105, 19.562155, -10.473951), over Limsa Lominsa Lower Decks.</summary>
    private static readonly Vector3 SceneAnchor = new(-94.725105f, 44.562155f, -10.473951f);

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
        new("Crane shot, look ahead 0: turns smoothly through straight up", Travel(Crane, lookAhead: 0f), 0),
        new("Crane shot, look ahead 0.5: turns smoothly through straight up", Travel(Crane), 0),
        new(
            "Climb drifting across straight up: no flip",
            Travel([P(0f, -10f, 0f), P(0.4f, 0f, 0f), P(0f, 10f, 0f)]),
            0
        ),
        new("Sharp turn in a vertical plane: no flip", Travel([P(8f, 5f, 0f), P(-7f, 0f, 0f), P(8f, -6f, 0f)]), 0),
        new("Hold partway up a climb: yaw holds still", TrackEditing.SetHold(Travel(Crane), 2, 2f), 0),
        new("Hairpin: turns smoothly", Travel([P(7f, 0f, -2.5f), P(-8f, 0f, 0f), P(7f, 0f, 2.5f)]), 0),
        new(
            "Into and out of a hold: smooth",
            TrackEditing.SetHold(Travel([P(-10f, 0f, 5f), P(0f, 0f, -5f), P(10f, 0f, 5f)]), 1, 2f),
            0
        ),
        new("Easing into the last point: settles without a step", EasingIntoTheEnd(), 0),
        new("Recorded aim with field of view and roll: no pop on arrival", RecordedAim(), 0),
        new("Straight doubleback: snaps round once", Travel(Doubleback), 1),
        new("Reversal, look ahead 0: snaps round once", Travel(Doubleback, lookAhead: 0f), 1),
    ];

    /// <summary>Four 20-yalm legs along +z, away from the grid, then 15 back at 179°: reverting the hold fix, the float wobble 80 yalms along turns a look ahead 0 aim at that turn by about 0.4°.</summary>
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
    internal static string FilePath()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(folder.FullName, "Vista.sln")))
            folder = folder.Parent ?? throw new DirectoryNotFoundException("The test run isn't inside the repository.");
        return Path.Combine(folder.FullName, "tests", "scenes", FileName);
    }

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
    private static Track Travel(ControlPoint[] points, float lookAhead = TrackEditing.DefaultLookAhead)
    {
        var track = TrackEditing.Empty(AimMode.PathTangent);
        foreach (var point in points)
            track = TrackEditing.Append(track, point);
        return TrackEditing.SetLookAhead(track, lookAhead);
    }

    /// <summary>A gentle curve whose last leg eases out into a hold at the end.</summary>
    private static Track EasingIntoTheEnd()
    {
        var track = Travel([P(-10f, 0f, 0f), P(0f, 0f, -4f), P(10f, 0f, -2f)]);
        return TrackEditing.SetHold(LegEasing.Set(track, 2, Easing.EaseOut), 2, 1.5f);
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
