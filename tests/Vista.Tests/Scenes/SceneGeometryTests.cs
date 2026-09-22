using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Scenes;

public class SceneGeometryTests
{
    private const float Tolerance = 1e-4f;

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    private static ControlPoint Point(float x, float y = 0f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    // One track with points at x = 0, 10, 20, local to a track anchor at (5, 0, 0) yaw 0.5, under a scene anchor at (100, 2, 50) yaw 1.
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
        var anchor = scene.Anchor.ToWorld(scene.Tracks[0].Anchor);

        Near(anchor.ToWorld(new Vector3(10f, 0f, 0f)), world.Points[1].Position);
        Assert.Equal(1.5f, world.Points[1].Yaw, Tolerance);
        Assert.Same(scene.Tracks[0].Timing, world.Timing);
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
    public void PlaceForPutsBothAnchorsUnderTheFirstPointAtFootHeight()
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
        Near(new Vector3(120f, 3f, 40f), SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Position);
        Assert.Equal(0f, SceneGeometry.WorldAnchor(placed, SceneEditing.Get(placed, id)).Yaw, Tolerance);
    }

    [Fact]
    public void PlaceForLeavesPlacedAnchorsAlone()
    {
        var scene = Anchored();
        Assert.Same(scene, SceneGeometry.PlaceFor(scene, scene.Tracks[0].Id, new Vector3(-9f, 0f, 9f), 0f));
    }

    [Fact]
    public void MovingTheSceneAnchorCarriesEveryTrack()
    {
        var scene = Anchored();
        var to = new Anchor(new Vector3(0f, 0f, 0f), 0f);
        var moved = SceneGeometry.MoveSceneAnchor(scene, to, carry: true);

        Assert.Equal(to, moved.Anchor);
        Assert.Same(scene.Tracks[0], moved.Tracks[0]);
        Near(to.ToWorld(scene.Tracks[0].Anchor).ToWorld(new Vector3(20f, 0f, 0f)), WorldPositions(moved)[2]);
    }

    [Fact]
    public void MovingTheSceneAnchorAloneLeavesEveryPointInTheWorld()
    {
        var scene = Anchored();
        var before = WorldPositions(scene);
        var moved = SceneGeometry.MoveSceneAnchor(scene, new Anchor(new Vector3(-30f, 1f, 8f), -0.7f), carry: false);

        for (var i = 0; i < before.Count; i++) Near(before[i], WorldPositions(moved)[i]);
        Assert.Equal(-0.7f, moved.Anchor.Yaw);
    }

    [Fact]
    public void MovingATrackAnchorCarriesItsPoints()
    {
        var scene = Anchored();
        var to = new Anchor(new Vector3(90f, 2f, 60f), 0.2f);
        var moved = SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, to, carry: true);

        Near(to.Position, SceneGeometry.WorldAnchor(moved, moved.Tracks[0]).Position);
        Assert.Same(scene.Tracks[0].Points, moved.Tracks[0].Points);
        Near(to.ToWorld(new Vector3(10f, 0f, 0f)), WorldPositions(moved)[1]);
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
            Near(before[i], world.Points[i].Position);
            Assert.Equal(worldYawsBefore[i], world.Points[i].Yaw, Tolerance);
        }
    }

    [Fact]
    public void MovingAnAnchorMarksItPlaced()
    {
        var scene = SceneEditing.New();
        Assert.True(SceneGeometry.MoveSceneAnchor(scene, new Anchor(Vector3.One, 0f), carry: true).AnchorPlaced);
        Assert.True(SceneGeometry.MoveTrackAnchor(scene, scene.Tracks[0].Id, new Anchor(Vector3.One, 0f), carry: true).Tracks[0].AnchorPlaced);
    }
}
