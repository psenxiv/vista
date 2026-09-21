using CinematicCam.Core.Camera;
using Xunit;

namespace CinematicCam.Tests.Camera;

public class CameraOwnershipTests
{
    [Fact]
    public void StartsUnowned()
    {
        Assert.False(new CameraOwnership().IsOwned);
    }

    [Fact]
    public void TakeMakesItOwned()
    {
        var ownership = new CameraOwnership();
        ownership.Take();
        Assert.True(ownership.IsOwned);
    }

    [Fact]
    public void ReleaseRecordsTheReason()
    {
        var ownership = new CameraOwnership();
        ownership.Take();
        ownership.Release("zone change");

        Assert.False(ownership.IsOwned);
        Assert.Equal("zone change", ownership.LastReleaseReason);
    }

    [Fact]
    public void ReleaseWhenAlreadyUnownedIsSafe()
    {
        var ownership = new CameraOwnership();
        ownership.Release("panic");
        ownership.Release("panic");
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void TakingAgainAfterReleaseClearsTheReason()
    {
        var ownership = new CameraOwnership();
        ownership.Take();
        ownership.Release("panic");
        ownership.Take();

        Assert.True(ownership.IsOwned);
        Assert.Null(ownership.LastReleaseReason);
    }
}
