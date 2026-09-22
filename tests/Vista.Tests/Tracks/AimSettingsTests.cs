using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class AimSettingsTests
{
    private const float Tolerance = 1e-4f;

    // A camera at (100, 5, 100) looking along yaw 90°, which faces −x.
    private static readonly ControlPoint Camera = new(new Vector3(100f, 5f, 100f), MathF.PI / 2f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void ANewTrackHasNoLookAtNoCharacterAndTheDefaultWatchSettings()
    {
        var track = TrackEditing.Empty();

        Assert.False(track.LookAtPlaced);
        Assert.Null(track.TargetName);
        Assert.Null(track.TargetWorld);
        Assert.Equal(1.3f, track.AimHeight);
        Assert.Equal(0.3f, track.Smoothing);
    }

    [Fact]
    public void ChoosingLookAtFirstPlacesItTenYalmsAlongTheFirstPointsAim()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), new ControlPoint(new Vector3(1f, 2f, 3f), MathF.PI / 2f, 0f, 1f));

        var looking = TrackEditing.SetAim(track, AimMode.LookAt, Camera);

        Assert.Equal(AimMode.LookAt, looking.Aim);
        Assert.True(looking.LookAtPlaced);
        Near(new Vector3(-9f, 2f, 3f), looking.LookAt);
    }

    [Fact]
    public void WithNoPointsTheLookAtGoesTenYalmsAheadOfTheCamera()
        => Near(new Vector3(90f, 5f, 100f), TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera).LookAt);

    [Fact]
    public void ComingBackToLookAtKeepsThePointWhereItWas()
    {
        var moved = TrackEditing.SetLookAt(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera), new Vector3(7f, 8f, 9f));

        var back = TrackEditing.SetAim(TrackEditing.SetAim(moved, AimMode.AimKeys, Camera), AimMode.LookAt, Camera);

        Assert.Equal(new Vector3(7f, 8f, 9f), back.LookAt);
    }

    [Fact]
    public void ALookAtThatIsNotFiniteChangesNothing()
    {
        var track = TrackEditing.Empty();

        Assert.Same(track, TrackEditing.SetLookAt(track, new Vector3(float.NaN, 0f, 0f)));
        Assert.Same(track, TrackEditing.SetLookAt(track, new Vector3(0f, float.PositiveInfinity, 0f)));
        Assert.Same(track, TrackEditing.SetLookAt(track, new Vector3(0f, 0f, float.NegativeInfinity)));
    }

    [Fact]
    public void OtherModesPlaceNoLookAtAndTheSameModeChangesNothing()
    {
        Assert.False(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.WatchTarget, Camera).LookAtPlaced);
        var track = TrackEditing.Empty();
        Assert.Same(track, TrackEditing.SetAim(track, AimMode.AimKeys, Camera));
    }

    [Fact]
    public void TheWatchSettingsClampAndAnEmptyNameClearsTheCharacter()
    {
        var track = TrackEditing.Empty();
        var named = TrackEditing.SetTarget(track, "Guard", null);

        Assert.Equal("Guard", named.TargetName);
        Assert.Null(TrackEditing.SetTarget(named, "", null).TargetName);
        Assert.Same(track, TrackEditing.SetTarget(track, null, null));
        Assert.Equal(3f, TrackEditing.SetAimHeight(track, 9f).AimHeight);
        Assert.Equal(0f, TrackEditing.SetAimHeight(track, -1f).AimHeight);
        Assert.Same(track, TrackEditing.SetAimHeight(track, float.NaN));
        Assert.Equal(1f, TrackEditing.SetSmoothing(track, 2f).Smoothing);
        Assert.Equal(0f, TrackEditing.SetSmoothing(track, -0.5f).Smoothing);
        Assert.Same(track, TrackEditing.SetSmoothing(track, 0.3f));
    }

    [Fact]
    public void ClearForgetsTheLookAtAndTheCharacter()
    {
        var track = TrackEditing.SetTarget(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera), "Aya", "Gilgamesh");

        var cleared = TrackEditing.Clear(track);

        Assert.False(cleared.LookAtPlaced);
        Assert.Null(cleared.TargetName);
        Assert.Null(cleared.TargetWorld);
    }

    [Fact]
    public void SetTargetNamesAPlayerByNameAndWorld()
    {
        var track = TrackEditing.SetTarget(TrackEditing.Empty(), "Aya", "Gilgamesh");

        Assert.Equal("Aya", track.TargetName);
        Assert.Equal("Gilgamesh", track.TargetWorld);
        Assert.Same(track, TrackEditing.SetTarget(track, "Aya", "Gilgamesh"));
        Assert.Equal("Cactuar", TrackEditing.SetTarget(track, "Aya", "Cactuar").TargetWorld);
    }

    [Fact]
    public void AnNpcHasNoWorldAndChoosingNoneForgetsTheWorld()
    {
        var player = TrackEditing.SetTarget(TrackEditing.Empty(), "Aya", "Gilgamesh");

        var npc = TrackEditing.SetTarget(player, "Guard", null);
        Assert.Equal("Guard", npc.TargetName);
        Assert.Null(npc.TargetWorld);
        Assert.Null(TrackEditing.SetTarget(player, "Aya", " ").TargetWorld);

        var none = TrackEditing.SetTarget(player, null, "Gilgamesh");
        Assert.Null(none.TargetName);
        Assert.Null(none.TargetWorld);
    }

    [Fact]
    public void FollowSwitchesDefaultToTurningAndLooking()
    {
        var track = TrackEditing.Empty();
        Assert.True(track.FollowTurns);
        Assert.True(track.FollowLooks);
    }

    [Fact]
    public void FollowSwitchesSetAndKeepTheSameInstanceWhenUnchanged()
    {
        var track = TrackEditing.Empty();
        var off = TrackEditing.SetFollowTurns(track, false);
        Assert.False(off.FollowTurns);
        Assert.Same(off, TrackEditing.SetFollowTurns(off, false));
        Assert.False(TrackEditing.SetFollowLooks(track, false).FollowLooks);
    }
}
