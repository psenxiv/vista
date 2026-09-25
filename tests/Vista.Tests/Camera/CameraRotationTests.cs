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

    // The upright up at (yaw, pitch), worked out independently of CameraRotation: the pitch-derivative of the facing.
    private static Vector3 UprightUp(float yaw, float pitch) =>
        new(MathF.Sin(yaw) * MathF.Sin(pitch), MathF.Cos(pitch), MathF.Cos(yaw) * MathF.Sin(pitch));

    // Direction is the view direction's convention, measured in game and pinned below; FromAngles must keep it, so it is
    // the reference here rather than a derived literal. The upright up at (yaw, pitch) is the pitch-derivative of that direction,
    // (sin yaw·sin pitch, cos pitch, cos yaw·sin pitch), world up made square to the facing.
    [Theory]
    [MemberData(nameof(AnglePairs))]
    public void MatchesTheExistingDirectionAndLevelsTheUp(float yaw, float pitch)
    {
        var rotation = CameraRotation.FromAngles(yaw, pitch, 0f);

        Near(CameraRotation.Direction(yaw, pitch), CameraRotation.Forward(rotation), 1e-5f);

        Near(UprightUp(yaw, pitch), CameraRotation.Up(rotation), 1e-5f);
    }

    // Roll turns the upright up U about the facing f, positive rolling right: U cos r + (f × U) sin r.
    [Theory]
    [InlineData(0.5f)]
    [InlineData(-2f)]
    [InlineData(MathF.PI)]
    public void RollTurnsTheUprightUpAboutTheFacing(float roll)
    {
        const float yaw = MathF.PI / 2f;
        const float pitch = 0.3f;

        var rotation = CameraRotation.FromAngles(yaw, pitch, roll);
        var forward = CameraRotation.Direction(yaw, pitch);
        var upright = UprightUp(yaw, pitch);

        var expectedUp = (upright * MathF.Cos(roll)) + (Vector3.Cross(forward, upright) * MathF.Sin(roll));
        Near(expectedUp, CameraRotation.Up(rotation), 1e-5f);
    }

    [Fact]
    public void DirectionFacesMinusZAtYawZeroTurnsTowardMinusXAndPitchesUp()
    {
        // (−sin yaw·cos pitch, sin pitch, −cos yaw·cos pitch): yaw 0 is (0, 0, −1), a quarter turn is (−1, 0, 0), and 30° up
        // is (0, 0.5, −0.8660254).
        Near(new Vector3(0f, 0f, -1f), CameraRotation.Direction(0f, 0f), 1e-6f);
        Near(new Vector3(-1f, 0f, 0f), CameraRotation.Direction(QuarterTurn, 0f), 1e-6f);
        Near(new Vector3(0f, 0.5f, -0.8660254f), CameraRotation.Direction(0f, 30f * Deg), 1e-6f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.2f, -0.3f)]
    [InlineData(-2.7f, 0.6f)]
    [InlineData(3.0f, 0.9f)]
    public void YawPitchInvertsDirection(float yaw, float pitch)
    {
        var (roundYaw, roundPitch) = CameraRotation.YawPitch(CameraRotation.Direction(yaw, pitch));

        Assert.Equal(yaw, roundYaw, 1e-5f);
        Assert.Equal(pitch, roundPitch, 1e-5f);
    }

    [Fact]
    public void YawPitchReadsAFacingOfAnyLength()
    {
        // (0, 10, −10) is 45° up along −z; (−20, 0, 0) is level, a quarter turn round.
        var (upYaw, upPitch) = CameraRotation.YawPitch(new Vector3(0f, 10f, -10f));
        var (roundYaw, roundPitch) = CameraRotation.YawPitch(new Vector3(-20f, 0f, 0f));

        Assert.Equal(0f, upYaw, 1e-6f);
        Assert.Equal(45f * Deg, upPitch, 1e-6f);
        Assert.Equal(QuarterTurn, roundYaw, 1e-6f);
        Assert.Equal(0f, roundPitch, 1e-6f);
    }

    [Fact]
    public void PitchJustShortOfStraightUpKeepsItsTilt()
    {
        // (0, 1, −1e-4) is atan(1e-4) ≈ 1e-4 short of straight up: pitch π/2 − 1e-4 = 1.5706963. Its unit y,
        // 1/√(1 + 1e-8), rounds to 1 in single precision, so asin(y) would read exactly π/2 and lose the tilt.
        Assert.Equal(1.5706963f, CameraRotation.YawPitch(new Vector3(0f, 1f, -1e-4f)).Pitch, 1e-6f);
    }

    [Fact]
    public void RollUpTurnsTheUpRightAboutTheFacing()
    {
        // Facing −z, rolling a quarter turn right carries up (0, 1, 0) onto the right, (1, 0, 0).
        Near(Vector3.UnitX, CameraRotation.RollUp(Vector3.UnitY, new Vector3(0f, 0f, -1f), QuarterTurn), 1e-6f);
    }

    [Fact]
    public void UprightLeansBackFromAnUpwardFacing()
    {
        // Facing (3, 4, 0)/5, 53° up along +x: world up made square to it is (0, 1, 0) - (3, 4, 0)·4/25 = (-12, 9, 0)/25,
        // which normalises to (-0.8, 0.6, 0): leaning back, never forward.
        Near(new Vector3(-0.8f, 0.6f, 0f), CameraRotation.Upright(new Vector3(3f, 4f, 0f)), 1e-6f);
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
        AnyAngles.Sample(
            angles =>
            {
                var (yaw, pitch, roll) = angles;
                var (gotYaw, gotPitch, gotRoll) = CameraRotation.ToAngles(CameraRotation.FromAngles(yaw, pitch, roll));

                Assert.Equal(yaw, gotYaw, 1e-5f);
                Assert.Equal(pitch, gotPitch, 1e-5f);
                Assert.Equal(roll, gotRoll, 1e-5f);
            },
            print: Kept<(float Yaw, float Pitch, float Roll)>(angles =>
                $"{angles.Yaw:R} {angles.Pitch:R} {angles.Roll:R}"
            )
        );

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
