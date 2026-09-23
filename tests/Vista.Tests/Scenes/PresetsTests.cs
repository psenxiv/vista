using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class PresetsTests
{
    // "Crane": one point at local (2, 0, 0) yaw 0.25, Look At at local (0, 1, 3); its anchor faced yaw π/2.
    private static Preset Crane()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(name: "Crane"), Point(2f, yaw: 0.25f)) with { LookAt = new Vector3(0f, 1f, 3f), LookAtPlaced = true };
        return new Preset(track, MathF.PI / 2f);
    }

    private static readonly Vector3 Ground = new(10f, 5f, 20f);

    private static Track Added(Scene scene, Guid id) => SceneGeometry.InWorld(scene, SceneEditing.Get(scene, id));

    [Fact]
    public void FromKeepsTheTrackLocalAndTakesItsAnchorsWorldYaw()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 2f, 50f), 1f), AnchorPlaced = true };
        var track = TrackEditing.Append(scene.Tracks[0] with { Anchor = new Anchor(new Vector3(5f, 0f, 0f), 0.5f), AnchorPlaced = true }, Point(3f));
        scene = SceneEditing.Replace(scene, track);

        var preset = Presets.From(scene, track.Id);

        // Scene yaw 1 plus the track anchor's local yaw 0.5.
        Assert.Equal(1.5f, preset.Yaw, 1e-6f);
        Assert.Equal(Anchor.Origin, preset.Track.Anchor);
        Assert.Same(track.Points, preset.Track.Points);
    }

    [Fact]
    public void PlacingInAnUnplacedScenePutsBothAnchorsOnTheGround()
    {
        var (scene, id) = Presets.Place(SceneEditing.New(), Crane(), Ground);
        var world = Added(scene, id);

        Assert.True(scene.AnchorPlaced);
        Assert.Equal(new Anchor(Ground, 0f), scene.Anchor);
        // Turn((2, 0, 0), π/2) = (2·cos + 0·sin, 0, 0·cos − 2·sin) = (0, 0, −2), so (10, 5, 20) + (0, 0, −2).
        Near(new Vector3(10f, 5f, 18f), world.Points[0].Position, 1e-4f);
        // Point yaw 0.25 plus the anchor's π/2.
        Assert.Equal(1.8207963f, world.Points[0].Yaw, 1e-5f);
        // Turn((0, 1, 3), π/2) = (0 + 3·1, 1, 3·0 − 0) = (3, 1, 0), so (10, 5, 20) + (3, 1, 0).
        Near(new Vector3(13f, 6f, 20f), world.LookAt, 1e-4f);
    }

    [Fact]
    public void PlacingInAPlacedSceneLeavesItsAnchorAndPutsThePointsInTheSamePlace()
    {
        var anchor = new Anchor(new Vector3(100f, 2f, 50f), 1f);
        var (scene, id) = Presets.Place(SceneEditing.New() with { Anchor = anchor, AnchorPlaced = true }, Crane(), Ground);

        Assert.Equal(anchor, scene.Anchor);
        // The track's anchor is (10, 5, 20) yaw π/2 in the world whatever the scene anchor, so as above.
        Assert.Equal(MathF.PI / 2f, SceneGeometry.WorldAnchor(scene, SceneEditing.Get(scene, id)).Yaw, 1e-5f);
        Near(new Vector3(10f, 5f, 18f), Added(scene, id).Points[0].Position, 1e-4f);
    }

    [Fact]
    public void PlacingAppendsANewTrackWithANewId()
    {
        var before = SceneEditing.New();
        var preset = Crane();

        var (once, first) = Presets.Place(before, preset, Ground);
        var (twice, second) = Presets.Place(once, preset, Ground);

        Assert.Equal(3, twice.Tracks.Count);
        Assert.Same(before.Tracks[0], twice.Tracks[0]);
        Assert.Equal(first, twice.Tracks[1].Id);
        Assert.Equal(second, twice.Tracks[2].Id);
        Assert.NotEqual(preset.Track.Id, first);
        Assert.NotEqual(first, second);
        Assert.Equal("Crane", twice.Tracks[2].Name);
        Assert.True(twice.Tracks[2].AnchorPlaced);
    }

    [Fact]
    public void PlacingTheSceneAnchorLeavesAnotherTracksLookAtInTheWorld()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Replace(scene, scene.Tracks[0] with { LookAt = new Vector3(5f, 6f, 7f), LookAtPlaced = true });

        var (placed, _) = Presets.Place(scene, Crane(), Ground);

        // Unplaced, the scene anchor is the origin, so the Look At was at (5, 6, 7) in the world.
        Near(new Vector3(5f, 6f, 7f), SceneGeometry.InWorld(placed, placed.Tracks[0]).LookAt, 1e-4f);
    }
}
