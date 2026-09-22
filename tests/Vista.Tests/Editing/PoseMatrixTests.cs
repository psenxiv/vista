using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class PoseMatrixTests
{
    [Fact]
    public void ALevelUnrolledPoseFacingNorthIsTheIdentityRotation()
    {
        var m = PoseMatrix.From(new Vector3(1f, 2f, 3f), 0f, 0f, 0f);
        Assert.Equal(1f, m.M11, 5);
        Assert.Equal(1f, m.M22, 5);
        Assert.Equal(1f, m.M33, 5);
        Assert.Equal(0f, m.M12, 5);
        Assert.Equal(0f, m.M23, 5);
        Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(m.M41, m.M42, m.M43));
    }

    [Theory]
    [InlineData(0.7f, 0.3f, 0.4f)]
    [InlineData(-2.5f, -0.9f, -1.2f)]
    [InlineData(3.0f, 1.2f, 2.9f)]
    [InlineData(0f, 0f, 0f)]
    public void ToPoseInvertsFrom(float yaw, float pitch, float roll)
    {
        var position = new Vector3(-5f, 7f, 11f);
        var (p, y, pi, r) = PoseMatrix.ToPose(PoseMatrix.From(position, yaw, pitch, roll));

        Assert.Equal(position, p);
        Assert.Equal(yaw, y, 4);
        Assert.Equal(pitch, pi, 4);
        Assert.Equal(roll, r, 4);
    }

    public static IEnumerable<object[]> YawsNearThePiWrap()
    {
        yield return new object[] { MathF.PI };
        yield return new object[] { -MathF.PI + 1e-3f };
        yield return new object[] { MathF.PI - 1e-3f };
    }

    [Theory]
    [MemberData(nameof(YawsNearThePiWrap))]
    public void ToPoseInvertsFromAcrossTheYawWrap(float yaw)
    {
        const float pitch = 0.3f;
        const float roll = 0.4f;
        var position = new Vector3(-5f, 7f, 11f);
        var (p, y, pi, r) = PoseMatrix.ToPose(PoseMatrix.From(position, yaw, pitch, roll));

        Assert.Equal(position, p);
        Assert.True(MathF.Abs(WrappedDifference(yaw, y)) < 1e-4f);
        Assert.True(MathF.Abs(WrappedDifference(pitch, pi)) < 1e-4f);
        Assert.True(MathF.Abs(WrappedDifference(roll, r)) < 1e-4f);
    }

    private static float WrappedDifference(float expected, float actual)
        => MathF.IEEERemainder(expected - actual, MathF.Tau);

    [Fact]
    public void ToPoseIgnoresScaleAndClampsPitch()
    {
        var scaled = Matrix4x4.CreateScale(3f) * PoseMatrix.From(Vector3.Zero, 0.5f, 0.2f, 0.1f);
        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(scaled);
        Assert.Equal(0.5f, yaw, 4);
        Assert.Equal(0.2f, pitch, 4);
        Assert.Equal(0.1f, roll, 4);

        var straightUp = PoseMatrix.From(Vector3.Zero, 0f, 1.5707963f, 0f);
        Assert.True(PoseMatrix.ToPose(straightUp).Pitch <= TrackAim.PitchLimit);
    }
}
