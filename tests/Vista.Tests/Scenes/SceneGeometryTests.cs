using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class SceneGeometryTests
{
    // One track with points at x = 0, 10, 20, local to a track anchor at (5, 0, 0) yaw 0.5, under a scene anchor at (100, 2, 50) yaw 1.
    // The two together put the track's anchor at (102.701512, 2, 45.792645) yaw 1.5.
    private static Scene Anchored()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 1f), AnchorPlaced = true };
        var track = scene.Tracks[0] with { Anchor = new Anchor(new Vector3(5f, 0f, 0f), 0.5f), AnchorPlaced = true };
        foreach (var x in new[] { 0f, 10f, 20f }) track = TrackEditing.Append(track, Point(x));
        return SceneEditing.Replace(scene, track);
    }

    private static IReadOnlyList<Vector3> WorldPositions(Scene scene)
        => SceneGeometry.InWorld(scene, scene.Tracks[0]).Points.Select(p => p.Position).ToList();

    [Fact]
    public void InWorldPlacesPointsThroughBothAnchors()
    {
        var scene = Anchored();
        var world = SceneGeometry.InWorld(scene, scene.Tracks[0]);

        Near(new Vector3(103.408884f, 2f, 35.817695f), world.Points[1].Position, 1e-4f);
        Assert.Equal(1.5f, world.Points[1].Yaw, 1e-4f);
    }

    [Fact]
    public void InWorldIsTheTrackItselfAtTheOrigin()
    {
        var scene = SceneEditing.New();
        var track = TrackEditing.Append(scene.Tracks[0], Point(3f));
        Assert.Same(track, SceneGeometry.InWorld(SceneEditing.Replace(scene, track), track));
    }

    [Fact]
    public void TimingIsTheSameWhereverTheAnchorsAre()
    {
        var scene = Anchored();
        var local = new TrackEvaluator(scene.Tracks[0]);
        var world = new TrackEvaluator(SceneGeometry.InWorld(scene, scene.Tracks[0]));

        Assert.Equal(local.Duration, world.Duration, 4);
        Assert.Equal(local.LegSeconds(2), world.LegSeconds(2), 4);
    }

    [Fact]
    public void PlaceForPutsBothAnchorsUnderTheFirstPointAtGroundHeight()
    {
        var scene = SceneEditing.New();
        var placed = SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(7f, 9f, -3f), 2f);

        Assert.True(placed.AnchorPlaced);
        Assert.Equal(new Anchor(new Vector3(7f, 2f, -3f), 0f), placed.Anchor);
        Assert.True(placed.Tracks[0].AnchorPlaced);
        Assert.Equal(Anchor.Origin, placed.Tracks[0].Anchor);
    }

    [Fact]
    public void PlaceForPutsALaterTracksAnchorUnderItsPointRelativeToTheScene()
    {
        var scene = Anchored();
        var (added, id) = SceneEditing.Add(scene);
        var placed = SceneGeometry.PlaceFor(added, id, new Vector3(120f, 8f, 40f), 3f);

        Assert.Equal(scene.Anchor, placed.Anchor);
        Near(new Vector3(120f, 3f, 40f), SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Position, 1e-4f);
        Assert.Equal(0f, SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Yaw, 1e-4f);
    }

    [Fact]
    public void PlaceForLeavesPlacedAnchorsAlone()
    {
        var scene = Anchored();
        Assert.Same(scene, SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(-9f, 0f, 9f), 0f));
    }

    [Fact]
    public void MovingTheSceneAnchorAloneLeavesEveryPointInTheWorld()
    {
        var scene = Anchored();
        var before = WorldPositions(scene);
        var moved = SceneGeometry.MoveSceneAnchor(scene, new Anchor(new Vector3(-30f, 1f, 8f), -0.7f), carry: false);

        for (var i = 0; i < before.Count; i++) Near(before[i], WorldPositions(moved)[i], 1e-4f);
        Assert.Equal(-0.7f, moved.Anchor.Yaw);
    }

    [Fact]
    public void MovingATrackAnchorCarriesItsPoints()
    {
        var scene = Anchored();
        var to = new Anchor(new Vector3(90f, 2f, 60f), 0.2f);
        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, to, carry: true);

        Near(to.Position, SceneGeometry.WorldAnchor(moved, moved.Tracks[0]).Position, 1e-4f);
        Assert.Same(scene.Tracks[0].Points, moved.Tracks[0].Points);
        Near(new Vector3(99.800666f, 2f, 58.013307f), WorldPositions(moved)[1], 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorAloneLeavesItsPointsInTheWorld()
    {
        var scene = Anchored();
        var before = WorldPositions(scene);
        var worldYawsBefore = SceneGeometry.InWorld(scene, scene.Tracks[0]).Points.Select(p => p.Yaw).ToList();
        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(new Vector3(80f, 0f, 30f), 2f), carry: false);

        var world = SceneGeometry.InWorld(moved, moved.Tracks[0]);
        for (var i = 0; i < before.Count; i++)
        {
            Near(before[i], world.Points[i].Position, 1e-4f);
            Assert.Equal(worldYawsBefore[i], world.Points[i].Yaw, 1e-4f);
        }
    }

    [Fact]
    public void MovingAnAnchorMarksItPlaced()
    {
        var scene = SceneEditing.New();
        Assert.True(SceneGeometry.MoveSceneAnchor(scene, new Anchor(Vector3.One, 0f), carry: true).AnchorPlaced);
        Assert.True(SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(Vector3.One, 0f), carry: true).Tracks[0].AnchorPlaced);
    }

    // The track in Anchored() with its Look At point at (0, 3, −10) local to its anchor.
    private static Scene WithLookAt(Scene scene)
        => SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(0f, 3f, -10f), LookAtPlaced = true });

    private static Vector3 WorldLookAt(Scene scene) => SceneGeometry.InWorld(scene, scene.Tracks[0]).LookAt;

    [Fact]
    public void InWorldCarriesTheLookAtAndTheAnchorThroughBothAnchors()
    {
        var scene = WithLookAt(Anchored());
        var world = SceneGeometry.InWorld(scene, scene.Tracks[0]);

        Near(new Vector3(92.726562f, 5f, 45.085273f), world.LookAt, 1e-4f);
        Near(new Vector3(102.701512f, 2f, 45.792645f), world.Anchor.Position, 1e-4f);
        Assert.Equal(1.5f, world.Anchor.Yaw, 1e-4f);
    }

    [Fact]
    public void ATrackWithOnlyALookAtIsStillCarried()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 0f), AnchorPlaced = true };
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(1f, 0f, 0f), LookAtPlaced = true });

        Near(new Vector3(101f, 2f, 50f), WorldLookAt(scene), 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorCarriesItsLookAt()
    {
        var scene = WithLookAt(Anchored());
        var to = new Anchor(new Vector3(90f, 2f, 60f), 0.2f);

        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, to, carry: true);

        Near(new Vector3(88.013307f, 5f, 50.199334f), WorldLookAt(moved), 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorAloneLeavesItsLookAtInTheWorld()
    {
        var scene = WithLookAt(Anchored());
        var before = WorldLookAt(scene);

        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(new Vector3(80f, 0f, 30f), 2f), carry: false);

        Near(before, WorldLookAt(moved), 1e-4f);
    }

    [Fact]
    public void MovingTheSceneAnchorAloneLeavesTheLookAtInTheWorld()
    {
        var scene = WithLookAt(Anchored());
        var before = WorldLookAt(scene);

        var moved = SceneGeometry.MoveSceneAnchor(scene, new Anchor(new Vector3(-30f, 1f, 8f), -0.7f), carry: false);

        Near(before, WorldLookAt(moved), 1e-4f);
    }

    [Fact]
    public void PlacingTheAnchorsLeavesALookAtPlacedBeforeThemInTheWorld()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(5f, 6f, 7f), LookAtPlaced = true });

        var placed = SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(20f, 9f, -3f), 2f);

        Near(new Vector3(5f, 6f, 7f), WorldLookAt(placed), 1e-4f);
    }

    [Fact]
    public void PlacingATrackAnchorUnderAPlacedSceneAnchorLeavesItsLookAtInTheWorld()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 1f), AnchorPlaced = true };
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(5f, 6f, 7f), LookAtPlaced = true });
        var before = WorldLookAt(scene);

        var placed = SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(20f, 9f, -3f), 2f);

        Assert.True(placed.Tracks[0].AnchorPlaced);
        Near(before, WorldLookAt(placed), 1e-4f);
    }

    [Fact]
    public void MovingAFollowTracksAnchorAloneLeavesItsOffset()
    {
        // The anchored scene with its track under Follow Target and one point at (0, 2, 5).
        var anchored = Anchored();
        var scene = SceneEditing.Replace(anchored, anchored.Tracks[0] with { Aim = AimMode.FollowTarget, Points = [new ControlPoint(new Vector3(0f, 2f, 5f), 0f, 0f, 1f)], Timing = [new PointTiming()] });
        var track = scene.Tracks[0];

        var moved = SceneGeometry.MoveTrackAnchor(scene, track.Id, new Anchor(new Vector3(30f, 0f, 30f), 1f), carry: false);

        Assert.Equal(track.Points, moved.Tracks[0].Points);
    }
}
