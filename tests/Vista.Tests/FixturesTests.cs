using System.Numerics;
using Vista.Core.Camera;
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
}
