using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

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
        Assert.True(MathF.Abs(Angles.Delta(y, yaw)) < 1e-4f);
        Assert.True(MathF.Abs(Angles.Delta(pi, pitch)) < 1e-4f);
        Assert.True(MathF.Abs(Angles.Delta(r, roll)) < 1e-4f);
    }

    [Fact]
    public void ToPoseIgnoresScale()
    {
        var scaled = Matrix4x4.CreateScale(3f) * PoseMatrix.From(Vector3.Zero, 0.5f, 0.2f, 0.1f);
        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(scaled);
        Assert.Equal(0.5f, yaw, 4);
        Assert.Equal(0.2f, pitch, 4);
        Assert.Equal(0.1f, roll, 4);
    }

    [Fact]
    public void ToPoseReachesStraightUpWithNoClamp()
    {
        // Straight up is pi / 2 exactly; no 89 degree cap holds it back, and ToAngles' pole case returns roll 0.
        var straightUp = PoseMatrix.From(Vector3.Zero, 0.7f, MathF.PI / 2f, 0f);
        var (_, yaw, pitch, roll) = PoseMatrix.ToPose(straightUp);
        Assert.Equal(0.7f, yaw, 4);
        Assert.Equal(MathF.PI / 2f, pitch, 5);
        Assert.Equal(0f, roll, 4);
    }

    [Fact]
    public void ToPoseInvertsFromNearVertical()
    {
        // 1.55 rad (88.8 degrees) sits close to the pole, comfortably inside the new pi / 2 range, with no clamp to round it off.
        var position = new Vector3(-5f, 7f, 11f);
        var (p, y, pi, r) = PoseMatrix.ToPose(PoseMatrix.From(position, 0.6f, 1.55f, 3f));

        Assert.Equal(position, p);
        Assert.Equal(0.6f, y, 4);
        Assert.Equal(1.55f, pi, 4);
        Assert.Equal(3f, r, 4);
    }
}
