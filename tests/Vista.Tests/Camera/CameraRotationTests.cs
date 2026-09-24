using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Camera;

public class CameraRotationTests
{
    public static readonly TheoryData<float, float> AnglePairs = new()
    {
        { 0f, 0f },
        { MathF.PI / 2f, 0.3f },
        { -2f, -1.2f },
        { 3f, 1.5f },
    };

    // FreeCamMotion and CameraOrientation are the existing conventions for view direction and up; FromAngles must
    // keep them, so they are the reference here rather than a derived literal.
    [Theory]
    [MemberData(nameof(AnglePairs))]
    public void MatchesTheExistingDirectionAndUpConventions(float yaw, float pitch)
    {
        var rotation = CameraRotation.FromAngles(yaw, pitch, 0f);

        var expectedForward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));
        Near(expectedForward, CameraRotation.Forward(rotation), 1e-5f);

        var expectedUp = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, expectedForward));
        Near(expectedUp, CameraRotation.Up(rotation), 1e-5f);
    }

    // UpFor's roll parameter is the existing convention for rolling the up vector about the view direction.
    [Theory]
    [InlineData(0.5f)]
    [InlineData(-2f)]
    [InlineData(MathF.PI)]
    public void RollMatchesTheExistingUpForConvention(float roll)
    {
        const float yaw = MathF.PI / 2f;
        const float pitch = 0.3f;

        var rotation = CameraRotation.FromAngles(yaw, pitch, roll);
        var forward = Vector3.Normalize(FreeCamMotion.LookAtFrom(Vector3.Zero, yaw, pitch));

        var expectedUp = Vector3.Normalize(CameraOrientation.UpFor(Vector3.Zero, forward, roll));
        Near(expectedUp, CameraRotation.Up(rotation), 1e-5f);
    }

    private static readonly Gen<float> AnyYawOrRoll = Gen.Float[-MathF.PI + 0.05f, MathF.PI - 0.05f];
    private static readonly Gen<float> AnyPitch = Gen.Float[(-MathF.PI / 2f) + 0.05f, (MathF.PI / 2f) - 0.05f];

    private static readonly Gen<(float Yaw, float Pitch, float Roll)> AnyAngles = Gen.Select(
        AnyYawOrRoll,
        AnyPitch,
        AnyYawOrRoll,
        (yaw, pitch, roll) => (yaw, pitch, roll)
    );

    [Fact]
    [Trait("Category", "Property")]
    public void RoundTripReturnsTheOriginalAngles() =>
        AnyAngles.Sample(angles =>
        {
            var (yaw, pitch, roll) = angles;
            var (gotYaw, gotPitch, gotRoll) = CameraRotation.ToAngles(CameraRotation.FromAngles(yaw, pitch, roll));

            Assert.Equal(yaw, gotYaw, 1e-5f);
            Assert.Equal(pitch, gotPitch, 1e-5f);
            Assert.Equal(roll, gotRoll, 1e-5f);
        });

    [Fact]
    public void PitchPlusNinetyIsThePoleFacingStraightUp()
    {
        // The upright up at (yaw, pitch) is (sin yaw . sin pitch, cos pitch, cos yaw . sin pitch); at pitch +90
        // that's (sin 0.7, 0, cos 0.7) = (0.644217687, 0, 0.764842187) for yaw 0.7.
        var rotation = CameraRotation.FromAngles(0.7f, MathF.PI / 2f, 0f);

        Near(new Vector3(0f, 1f, 0f), CameraRotation.Forward(rotation), 1e-5f);
        Near(new Vector3(0.644217687f, 0f, 0.764842187f), CameraRotation.Up(rotation), 1e-5f);

        var (yaw, pitch, roll) = CameraRotation.ToAngles(rotation);
        Assert.Equal(0.7f, yaw, 1e-5f);
        Assert.Equal(MathF.PI / 2f, pitch, 1e-5f);
        Assert.Equal(0f, roll, 1e-5f);
    }

    [Fact]
    public void PitchMinusNinetyIsThePoleFacingStraightDown()
    {
        // Same derivation as the +90 pole, with sin(-90) = -1: up is (-sin 0.7, 0, -cos 0.7).
        var rotation = CameraRotation.FromAngles(0.7f, -MathF.PI / 2f, 0f);

        Near(new Vector3(0f, -1f, 0f), CameraRotation.Forward(rotation), 1e-5f);
        Near(new Vector3(-0.644217687f, 0f, -0.764842187f), CameraRotation.Up(rotation), 1e-5f);

        var (yaw, pitch, roll) = CameraRotation.ToAngles(rotation);
        Assert.Equal(0.7f, yaw, 1e-5f);
        Assert.Equal(-MathF.PI / 2f, pitch, 1e-5f);
        Assert.Equal(0f, roll, 1e-5f);
    }

    [Fact]
    public void TwistIsZeroForAPureYawTurn()
    {
        // At pitch 0 the upright up is (0, 1, 0) for every yaw (sin 0 . sin yaw term vanishes), so both frames
        // share the same up and the picture does not turn.
        var forwardA = new Vector3(0f, 0f, -1f);
        var forwardB = new Vector3(-MathF.Sin(1f), 0f, -MathF.Cos(1f));
        var up = new Vector3(0f, 1f, 0f);

        Assert.Equal(0f, CameraRotation.Twist(forwardA, up, forwardB, up), 1e-5f);
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

        Assert.Equal(0f, CameraRotation.Twist(forwardA, upA, forwardB, upB), 1e-5f);
    }

    [Fact]
    public void TwistMeasuresARollAboutAFixedForward()
    {
        var forward = new Vector3(0f, 0f, -1f);
        var upA = new Vector3(0f, 1f, 0f);
        var upB = Vector3.Transform(upA, Quaternion.CreateFromAxisAngle(forward, 0.3f));

        Assert.Equal(0.3f, CameraRotation.Twist(forward, upA, forward, upB), 1e-5f);
    }

    [Theory]
    [InlineData(1f, 2f, 3f)]
    [InlineData(-1f, 0.5f, 2f)]
    [InlineData(0f, 0f, -5f)]
    public void UprightIsUnitLengthAndSquareToTheForward(float x, float y, float z)
    {
        var forward = new Vector3(x, y, z);
        var up = CameraRotation.Upright(forward);

        Assert.Equal(1f, up.Length(), 1e-5f);
        Assert.Equal(0f, Vector3.Dot(Vector3.Normalize(forward), up), 1e-5f);
    }

    [Fact]
    public void UprightFallsBackAtExactVertical()
    {
        // Matches FromAngles(0, +-90deg, 0)'s up: at yaw 0, pitch +90 gives (sin 0, 0, cos 0) = (0, 0, 1);
        // pitch -90 gives (-sin 0, 0, -cos 0) = (0, 0, -1).
        Near(new Vector3(0f, 0f, 1f), CameraRotation.Upright(new Vector3(0f, 1f, 0f)), 1e-5f);
        Near(new Vector3(0f, 0f, -1f), CameraRotation.Upright(new Vector3(0f, -1f, 0f)), 1e-5f);
    }

    [Fact]
    public void FromBasisSquaresAnAlreadyPerpendicularBasis()
    {
        // Forward +X, up +Y are already square (dot 0), so both pass through unchanged.
        var rotation = CameraRotation.FromBasis(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));

        Near(new Vector3(1f, 0f, 0f), CameraRotation.Forward(rotation), 1e-5f);
        Near(new Vector3(0f, 1f, 0f), CameraRotation.Up(rotation), 1e-5f);
    }

    [Fact]
    public void FromBasisFallsBackToUprightWhenUpIsParallelToForward()
    {
        // Up parallel (here, equal) to forward squares to the zero vector, so it falls back to Upright(forward),
        // which for straight up is (0, 0, 1) (see UprightFallsBackAtExactVertical).
        var forward = new Vector3(0f, 1f, 0f);
        var rotation = CameraRotation.FromBasis(forward, forward);

        Near(forward, CameraRotation.Forward(rotation), 1e-5f);
        Near(new Vector3(0f, 0f, 1f), CameraRotation.Up(rotation), 1e-5f);
    }
}
