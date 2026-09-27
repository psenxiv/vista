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
    public void AGeneratedTrackWhoseSpotPassesThroughTheCameraIsLeftOut()
    {
        // The look ahead is the loop's length from the crossing back to it, so the spot passes through the camera there.
        var crossing = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Hairpin crossing", StringComparison.Ordinal)
        );
        Assert.True(SpotPassesThroughCamera(crossing.Track));
    }

    [Fact]
    public void ATrackWhoseSpotCarriesOnPastTheEndIsKept()
    {
        // Straight along +x, the spot 3 yalms ahead carries on straight past the end, so the gap is 3 yalms throughout.
        var track = TrackEditing.SetLookAhead(
            TrackThrough([Point(0f), Point(10f), Point(20f)], AimMode.PathTangent, 2f),
            3f
        );
        Assert.False(SpotPassesThroughCamera(track));
    }

    [Fact]
    public void ALapBackToTheStartWithinTheLookAheadIsKept()
    {
        // The lap is shorter than the look ahead, so at the start the spot is past the end, carried on beyond the camera,
        // and it stays ahead of it as it sets off.
        var lap = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Lap back to the start", StringComparison.Ordinal)
        );
        Assert.False(SpotPassesThroughCamera(lap.Track));
    }

    [Fact]
    public void ALapHeldAtItsStartIsKept()
    {
        // As above, held first: the spot stays beyond the camera through the hold and as it sets off.
        var lap = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Lap held at its start", StringComparison.Ordinal)
        );
        Assert.False(SpotPassesThroughCamera(lap.Track));
    }
}
