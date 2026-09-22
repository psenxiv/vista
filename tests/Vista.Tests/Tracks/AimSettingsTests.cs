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
    public void ANewTrackHasNoLookAtNoCharacterAndTheDefaultFollowSettings()
    {
        var track = TrackEditing.Empty();

        Assert.False(track.LookAtPlaced);
        Assert.Null(track.TargetName);
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
    public void OtherModesPlaceNoLookAtAndTheSameModeChangesNothing()
    {
        Assert.False(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.FollowTarget, Camera).LookAtPlaced);
        var track = TrackEditing.Empty();
        Assert.Same(track, TrackEditing.SetAim(track, AimMode.AimKeys, Camera));
    }

    [Fact]
    public void TheFollowSettingsClampAndAnEmptyNameClearsTheCharacter()
    {
        var track = TrackEditing.Empty();
        var named = TrackEditing.SetTarget(track, "Guard");

        Assert.Equal("Guard", named.TargetName);
        Assert.Null(TrackEditing.SetTarget(named, "").TargetName);
        Assert.Same(track, TrackEditing.SetTarget(track, null));
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
        var track = TrackEditing.SetTarget(TrackEditing.SetAim(TrackEditing.Empty(), AimMode.LookAt, Camera), "Guard");

        var cleared = TrackEditing.Clear(track);

        Assert.False(cleared.LookAtPlaced);
        Assert.Null(cleared.TargetName);
    }
}
