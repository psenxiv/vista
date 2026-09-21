using System.Numerics;
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

public class CameraOrientationTests
{
    [Fact]
    public void MatchesUpVectorMeasuredInGame()
    {
        // Captured 2026-09-21 from a live camera while position and look-at were held.
        var position = new Vector3(-381.3313f, -57.192963f, 133.87306f);
        var lookAt = new Vector3(-380.7148f, -58.10948f, 136.4235f);
        var expected = new Vector3(0.07314417f, 0.8912578f, 0.30260092f);

        var up = CameraOrientation.UpFor(position, lookAt);

        Assert.Equal(expected.X, up.X, 4);
        Assert.Equal(expected.Y, up.Y, 4);
        Assert.Equal(expected.Z, up.Z, 4);
    }

    [Fact]
    public void UpIsPerpendicularToTheViewDirection()
    {
        var position = new Vector3(10, 20, 30);
        var lookAt = new Vector3(-5, 12, 44);

        var forward = Vector3.Normalize(lookAt - position);
        var up = CameraOrientation.UpFor(position, lookAt);

        Assert.Equal(0f, Vector3.Dot(forward, up), 5);
    }

    [Fact]
    public void LevelViewGivesWorldUp()
    {
        var up = CameraOrientation.UpFor(Vector3.Zero, new Vector3(0, 0, -10));

        Assert.Equal(0f, up.X, 5);
        Assert.Equal(1f, up.Y, 5);
        Assert.Equal(0f, up.Z, 5);
    }

    [Theory]
    [InlineData(0f, -1f, 0f)]  // straight down
    [InlineData(0f, 1f, 0f)]   // straight up
    public void VerticalViewDoesNotCollapse(float x, float y, float z)
    {
        var up = CameraOrientation.UpFor(Vector3.Zero, new Vector3(x, y, z) * 10f);

        Assert.False(float.IsNaN(up.X) || float.IsNaN(up.Y) || float.IsNaN(up.Z));
        Assert.True(up.Length() > 0.5f, $"up collapsed to {up}");
        Assert.Equal(0f, Vector3.Dot(Vector3.Normalize(new Vector3(x, y, z)), up), 5);
    }

    [Fact]
    public void CoincidentPositionAndLookAtDoesNotThrow()
    {
        var up = CameraOrientation.UpFor(Vector3.One, Vector3.One);

        Assert.False(float.IsNaN(up.X) || float.IsNaN(up.Y) || float.IsNaN(up.Z));
    }
}
