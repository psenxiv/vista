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
    public void TwistIsZeroForAPureYawTurn()
    {
        // At pitch 0 the upright up is (0, 1, 0) for every yaw (sin 0 . sin yaw term vanishes), so both frames
        // share the same up and the picture does not turn.
        var forwardA = new Vector3(0f, 0f, -1f);
        var forwardB = new Vector3(-MathF.Sin(1f), 0f, -MathF.Cos(1f));
        var up = new Vector3(0f, 1f, 0f);

        Assert.Equal(0f, Twist(forwardA, up, forwardB, up), 1e-5f);
    }

    [Fact]
    public void TwistIsZeroForAPurePitchTurn()
    {
        // Going from pitch 0 to pitch 0.5 at yaw 0 is a rotation about world +X by 0.5 rad: forward (0,0,-1) ->
        // (0, sin 0.5, -cos 0.5), and the same rotation carries up (0,1,0) -> (0, cos 0.5, sin 0.5), which is
        // exactly the upright up at pitch 0.5 (cos 0.5 . sin 0 for X, cos 0.5 for Y, cos 0 . sin 0.5 for Z).
        var forwardA = new Vector3(0f, 0f, -1f);
        var upA = new Vector3(0f, 1f, 0f);
        var forwardB = new Vector3(0f, 0.479425539f, -0.877582562f);
        var upB = new Vector3(0f, 0.877582562f, 0.479425539f);

        Assert.Equal(0f, Twist(forwardA, upA, forwardB, upB), 1e-5f);
    }

    [Fact]
    public void TwistMeasuresARollAboutAFixedForward()
    {
        var forward = new Vector3(0f, 0f, -1f);
        var upA = new Vector3(0f, 1f, 0f);
        var upB = Vector3.Transform(upA, Quaternion.CreateFromAxisAngle(forward, 0.3f));

        Assert.Equal(0.3f, Twist(forward, upA, forward, upB), 1e-5f);
    }

    // Looking along -Z with up +Y until 0.5 s, then with an up that isn't a number.
    private static CameraState UpLostAtHalfASecond(double t) =>
        new(
            Vector3.Zero,
            new Vector3(0f, 0f, -10f),
            t < 0.5 ? Vector3.UnitY : new Vector3(float.NaN, float.NaN, float.NaN),
            1f
        );

    [Fact]
    public void AnUpThatIsNotANumberIsAPictureStep() =>
        Assert.Contains(PictureSteps(UpLostAtHalfASecond, 1.0), step => float.IsNaN(step.Size));

    [Fact]
    public void AnUpThatIsNotANumberMakesTheLargestTwistNotANumber() =>
        Assert.True(float.IsNaN(LargestTwist(UpLostAtHalfASecond, 1.0)));

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
        // At the start of the lap, the spot 2 s ahead waits in the hold where the camera is: it isn't moving, so it counts.
        var lap = RegressionScene.Cases.Single(c =>
            c.Name.StartsWith("Lap back to the start", StringComparison.Ordinal)
        );
        Assert.False(SpotPassesThroughCamera(lap.Track));
    }
}
