using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class PointTransferTests
{
    private static readonly PointTiming[] SourceTiming =
    [
        new(Hold: 1f),
        new(LegSpeed: 2f, Hold: 0.5f, InMode: TangentMode.Linear),
        new(LegSpeed: 3f, InMode: TangentMode.Manual, OutMode: TangentMode.Manual, InTangent: 0.25f, OutTangent: 0.75f, Broken: true),
        new(LegSpeed: 4f, OutMode: TangentMode.Flat),
    ];

    // In the world: turned π/2 about B's anchor at (110, 0, 20), (5, 3, 0) off it is local (0, 3, 5), yaw 1 − π/2.
    private static readonly ControlPoint W1 = Point(115f, 3f, 20f, yaw: 1f, pitch: 0.2f, fov: 0.9f, roll: 0.1f);

    // (0, 4, −8) off B's anchor is local (8, 4, 0), yaw −π/2.
    private static readonly ControlPoint W2 = Point(110f, 4f, 12f);

    // Scene anchor (100, 0, 0) yaw 0. Track A: points at local x = 0, 10, 20, 30 with SourceTiming.
    // Track B: one point, anchor local (10, 0, 20) yaw π/2, so (110, 0, 20) yaw π/2 in the world.
    private static Scene TwoTracks()
    {
        var scene = SceneEditing.New() with { Anchor = new Anchor(new Vector3(100f, 0f, 0f), 0f), AnchorPlaced = true };
        var a = scene.Tracks[0] with { Name = "A", AnchorPlaced = true, Points = [Point(0f), Point(10f), Point(20f), Point(30f)], Timing = SourceTiming };
        var (added, id) = SceneEditing.Add(SceneEditing.Replace(scene, a));
        var b = SceneEditing.Get(added, id) with
        {
            Name = "B", Anchor = new Anchor(new Vector3(10f, 0f, 20f), MathF.PI / 2f), AnchorPlaced = true, Points = [Point(0f)], Timing = [new PointTiming()],
        };
        return SceneEditing.Replace(added, b);
    }

    private static float? NoGround(Vector3 _) => null;

    [Fact]
    public void PointsGoOnTheEndInSourceOrderRelativeToTheDestinationsAnchor()
    {
        var scene = TwoTracks();
        var (result, to, moved) = PointTransfer.Move(scene, scene.Tracks[0].Id, [2, 1], [W2, W1], scene.Tracks[1].Id, NoGround);

        var b = result.Tracks[1];
        Assert.Equal(scene.Tracks[1].Id, to);
        Assert.Equal([1, 2], moved);
        Assert.Equal(3, b.Points.Count);
        Assert.Equal(Point(0f), b.Points[0]);
        Near(new Vector3(0f, 3f, 5f), b.Points[1].Position, 1e-5f);
        Assert.Equal(-0.5707963f, b.Points[1].Yaw, 1e-6f);
        Assert.Equal(0.2f, b.Points[1].Pitch, 0f);
        Assert.Equal(0.9f, b.Points[1].Fov, 0f);
        Assert.Equal(0.1f, b.Points[1].Roll, 0f);
        Near(new Vector3(8f, 4f, 0f), b.Points[2].Position, 1e-5f);
        Assert.Equal(-1.5707963f, b.Points[2].Yaw, 1e-6f);
    }

    [Fact]
    public void TimingTravelsAndAPinnedLegStaysBetweenNeighbours()
    {
        var scene = TwoTracks();
        var b = PointTransfer.Move(scene, scene.Tracks[0].Id, [2, 1], [W2, W1], scene.Tracks[1].Id, NoGround).Scene.Tracks[1];

        Assert.Equal(new PointTiming(), b.Timing[0]);
        Assert.Equal(SourceTiming[1] with { LegSpeed = null }, b.Timing[1]);
        Assert.Equal(SourceTiming[2], b.Timing[2]);
    }

    [Fact]
    public void APinnedLegBetweenPointsThatWereApartFollowsTheTrackSpeed()
    {
        var scene = TwoTracks();
        var b = PointTransfer.Move(scene, scene.Tracks[0].Id, [0, 2], [W1, W2], scene.Tracks[1].Id, NoGround).Scene.Tracks[1];

        Assert.Equal(SourceTiming[0], b.Timing[1]);
        Assert.Equal(SourceTiming[2] with { LegSpeed = null }, b.Timing[2]);
    }

    [Fact]
    public void TheSourceClosesItsGapsAsDeletingWould()
    {
        var scene = TwoTracks();
        var a = PointTransfer.Move(scene, scene.Tracks[0].Id, [1, 2], [W1, W2], scene.Tracks[1].Id, NoGround).Scene.Tracks[0];

        // Deleting 2 merges legs 2 and 3 at leg 2's speed (3); deleting 1 then merges that with leg 1 at leg 1's (2).
        Assert.Equal([Point(0f), Point(30f)], a.Points);
        Assert.Equal([SourceTiming[0], SourceTiming[3] with { LegSpeed = 2f }], a.Timing);
    }

    [Fact]
    public void EveryPointCanLeaveTheSource()
    {
        var scene = TwoTracks();
        var world = new[] { W1, W2, Point(120f), Point(130f) };
        var (result, _, moved) = PointTransfer.Move(scene, scene.Tracks[0].Id, [0, 1, 2, 3], world, scene.Tracks[1].Id, NoGround);

        Assert.Empty(result.Tracks[0].Points);
        Assert.Empty(result.Tracks[0].Timing);
        Assert.Equal([1, 2, 3, 4], moved);
        Assert.Equal(new float?[] { null, null, 2f, 3f, 4f }, result.Tracks[1].Timing.Select(t => t.LegSpeed));
    }

    [Fact]
    public void NoDestinationAddsATrackAtTheEndAnchoredOnTheGroundUnderTheFirstPoint()
    {
        var scene = TwoTracks();
        var (result, to, moved) = PointTransfer.Move(scene, scene.Tracks[0].Id, [1], [W1], null, _ => 1.5f);

        var added = result.Tracks[2];
        Assert.Equal(3, result.Tracks.Count);
        Assert.Equal(added.Id, to);
        Assert.Equal("Track 3", added.Name);
        Assert.Equal([0], moved);

        // Ground under (115, 3, 20) is (115, 1.5, 20); less the scene anchor (100, 0, 0) that is (15, 1.5, 20), yaw 0.
        Assert.True(added.AnchorPlaced);
        Near(new Vector3(15f, 1.5f, 20f), added.Anchor.Position, 1e-5f);
        Assert.Equal(0f, added.Anchor.Yaw, 0f);
        Near(new Vector3(0f, 1.5f, 0f), added.Points[0].Position, 1e-5f);
        Assert.Equal(1f, added.Points[0].Yaw, 1e-6f);
        Assert.Equal(SourceTiming[1] with { LegSpeed = null }, added.Timing[0]);
    }

    [Fact]
    public void WithNoGroundTheNewAnchorIsAtThePointsHeight()
    {
        var scene = TwoTracks();
        var added = PointTransfer.Move(scene, scene.Tracks[0].Id, [1], [W1], null, NoGround).Scene.Tracks[2];

        Near(new Vector3(15f, 3f, 20f), added.Anchor.Position, 1e-5f);
        Near(Vector3.Zero, added.Points[0].Position, 1e-5f);
    }

    [Fact]
    public void AnEmptyUnplacedDestinationIsAnchoredUnderTheFirstPoint()
    {
        var (scene, empty) = SceneEditing.Add(TwoTracks());
        var added = PointTransfer.Move(scene, scene.Tracks[0].Id, [3], [W2], empty, _ => 1f).Scene.Tracks[2];

        // Ground under (110, 4, 12) is (110, 1, 12); less the scene anchor that is (10, 1, 12), and the point is 3 above it.
        Assert.True(added.AnchorPlaced);
        Near(new Vector3(10f, 1f, 12f), added.Anchor.Position, 1e-5f);
        Near(new Vector3(0f, 3f, 0f), added.Points[0].Position, 1e-5f);
    }

    [Fact]
    public void HiddenTracksAndThePlaylistAreLeftAlone()
    {
        var two = TwoTracks();
        var scene = SceneEditing.SetHidden(two, two.Tracks[1].Id, true) with { Playlist = [new PlaylistEntry(Guid.NewGuid(), two.Tracks[0].Id)] };
        var result = PointTransfer.Move(scene, scene.Tracks[0].Id, [0], [W1], scene.Tracks[1].Id, NoGround).Scene;

        Assert.Equal(scene.Hidden, result.Hidden);
        Assert.Equal(scene.Playlist, result.Playlist);
    }

    [Fact]
    public void AFollowTargetTrackTakesNoMovedPoints()
    {
        var two = TwoTracks();
        var scene = SceneEditing.Replace(two, TrackEditing.Clear(two.Tracks[1]) with { Aim = AimMode.FollowTarget });

        var refused = Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, scene.Tracks[0].Id, [0], [W1], scene.Tracks[1].Id, NoGround));
        Assert.Equal("A Follow Target track has one point", refused.Message);
    }

    [Fact]
    public void PointsAreNotMovedOntoTheirOwnTrack()
    {
        var scene = TwoTracks();
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, scene.Tracks[0].Id, [0], [W1], scene.Tracks[0].Id, NoGround));
    }

    [Fact]
    public void UnknownTracksAreRefused()
    {
        var scene = TwoTracks();
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, Guid.NewGuid(), [0], [W1], scene.Tracks[1].Id, NoGround));
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, scene.Tracks[0].Id, [0], [W1], Guid.NewGuid(), NoGround));
    }

    [Fact]
    public void BadPointListsAreRefused()
    {
        var scene = TwoTracks();
        var a = scene.Tracks[0].Id;
        var b = scene.Tracks[1].Id;
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, a, [], [], b, NoGround));
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, a, [1, 1], [W1, W2], b, NoGround));
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, a, [-1], [W1], b, NoGround));
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, a, [4], [W1], b, NoGround));
        Assert.Throws<ArgumentException>(() => PointTransfer.Move(scene, a, [0, 1], [W1], b, NoGround));
    }
}
