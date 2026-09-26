using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Tests.Regression;
using Xunit;
using Xunit.Sdk;
using static Vista.Tests.Fixtures;

namespace Vista.Tests;

public class FixturesTests
{
    [Fact]
    public void AMalformedFrameFailsNamingWhereAndTheRule()
    {
        // A field of view of 0 rad is below the 5° minimum, the only rule this frame breaks.
        var frame = new CameraState(Vector3.Zero, new Vector3(0f, 0f, -10f), Vector3.UnitY, 0f);

        var failure = Assert.Throws<FailException>(() => AssertWellFormed(frame, "Frame 3"));

        Assert.Equal("Frame 3: the field of view is out of range (0)", failure.Message);
    }

    [Fact]
    public void AGeneratedTrackWhoseSpotPassesThroughTheMovingCameraIsLeftOut()
    {
        var crossing = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Hairpin crossing", StringComparison.Ordinal)
        );
        Assert.True(SpotPassesThroughCamera(crossing.Track));
    }

    [Fact]
    public void ASpotWaitingWhereTheCameraStartsIsKept()
    {
        // At the start of the lap, 2 s of travel is past the end, so the spot waits there, where the camera is: it isn't
        // moving, so the track is kept.
        var lap = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Lap back to the start", StringComparison.Ordinal)
        );
        Assert.False(SpotPassesThroughCamera(lap.Track));
    }

    [Fact]
    public void AGeneratedTrackWhoseCameraReachesTheSpotWaitingAtTheEndIsLeftOut()
    {
        // The camera arrives at the held point where the path ends with the lap still to go, and turns round as it
        // moves off, by design.
        var held = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Held where the path ends", StringComparison.Ordinal)
        );
        Assert.True(SpotPassesThroughCamera(held.Track));
    }

    [Fact]
    public void APointNearTheEndLeadingIntoAPointOnItIsLeftOut()
    {
        // Point 1 is 0.07 yalm from the end, (0, 0, 0): at least 1e-3 and under 0.1. Point 2, a middle point, is on the end.
        // The path is about 31 yalms, 1.6 s at 20 yalms a second, under the 2 s look ahead, so the spot waits at the end
        // from the start, and about 21 yalms are left at point 1.
        var track = TrackEditing.SetLookAhead(
            TrackThrough(
                [Point(-10f), Point(0f, z: 0.07f), Point(0f), Point(10f, z: 3f), Point(0f)],
                AimMode.PathTangent,
                20f
            ),
            2f
        );
        Assert.True(NearTheEndBeforeAPointOnIt(track, new TrackEvaluator(track)));
        Assert.True(SpotPassesThroughCamera(track));
    }

    [Theory]
    [InlineData(0.07f)]
    [InlineData(0.09f)]
    public void APointBesideTheWaitingEndWithNoPointOnItIsKept(float dz)
    {
        // Out along x through (0, 0, 0) and back, ending dz yalm beside it: no point but the last is on the end, and the
        // camera passes the end no nearer than dz, more than 0.05.
        var track = TrackEditing.SetLookAhead(
            TrackThrough([Point(-10f), Point(0f), Point(10f), Point(0f, z: dz)], AimMode.PathTangent, 20f),
            2f
        );
        Assert.False(NearTheEndBeforeAPointOnIt(track, new TrackEvaluator(track)));
        Assert.False(SpotPassesThroughCamera(track));
    }

    [Fact]
    public void ASpotWaitingWhereTheCameraHoldsAtItsStartIsKept()
    {
        // Held at the start of the lap, the camera is under the waiting spot before it has moved, then leaves it.
        var lap = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Lap held at its start", StringComparison.Ordinal)
        );
        Assert.False(SpotPassesThroughCamera(lap.Track));
    }
}
