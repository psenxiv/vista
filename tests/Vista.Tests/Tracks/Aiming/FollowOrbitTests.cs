using System.Numerics;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Tracks.Aiming;

public class FollowOrbitTests
{
    private const float Quarter = MathF.PI / 2f;

    // Five yalms behind and two up, looking the way the character faces (yaw 0).
    private static readonly ControlPoint Behind = new(new Vector3(0f, 2f, 5f), 0f, 0.1f, 1f, 0.2f);

    [Fact]
    public void AnOffsetBehindIsAtAngleZero()
    {
        var orbit = FollowOrbit.Of(Behind);

        Assert.Equal(5f, orbit.Distance, 4);
        Assert.Equal(0f, orbit.Angle, 4);
        Assert.Equal(2f, orbit.Height, 4);
    }

    [Fact]
    public void AnglesRunRoundToTheRightThenInFrontAndStayPositive()
    {
        Assert.Equal(Quarter, FollowOrbit.Of(Behind with { Position = new Vector3(5f, 0f, 0f) }).Angle, 4);
        Assert.Equal(MathF.PI, FollowOrbit.Of(Behind with { Position = new Vector3(0f, 0f, -5f) }).Angle, 4);
        Assert.Equal(3f * Quarter, FollowOrbit.Of(Behind with { Position = new Vector3(-5f, 0f, 0f) }).Angle, 4);
    }

    [Fact]
    public void ChangingTheAngleSwingsThePointAndTurnsItsYaw()
    {
        var moved = FollowOrbit.With(Behind, new Orbit(5f, Quarter, 2f));

        Near(new Vector3(5f, 2f, 0f), moved.Position, 1e-4f);
        Assert.Equal(Quarter, moved.Yaw, 4);
        Assert.Equal(Behind.Pitch, moved.Pitch);
        Assert.Equal(Behind.Roll, moved.Roll);
        Assert.Equal(Behind.Fov, moved.Fov);
    }

    [Fact]
    public void DistanceAndHeightLeaveTheAngleAndTheAim()
    {
        var moved = FollowOrbit.With(Behind, new Orbit(8f, 0f, 3f));

        Near(new Vector3(0f, 3f, 8f), moved.Position, 1e-4f);
        Assert.Equal(Behind.Yaw, moved.Yaw);
    }

    [Fact]
    public void FromTheCharactersFeetTheAnglePlacesThePoint()
    {
        var onThem = Behind with { Position = Vector3.Zero };

        Near(new Vector3(0f, 1f, -4f), FollowOrbit.With(onThem, new Orbit(4f, MathF.PI, 1f)).Position, 1e-4f);
    }

    [Fact]
    public void DistanceNeverGoesBelowZero() =>
        Assert.Equal(0f, FollowOrbit.Of(FollowOrbit.With(Behind, new Orbit(-3f, 0f, 2f))).Distance, 4);
}
