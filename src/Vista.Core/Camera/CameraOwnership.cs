namespace Vista.Core.Camera;

/// <summary>Whether the plugin currently owns the camera. Releasing is always safe.</summary>
public sealed class CameraOwnership
{
    public bool IsOwned { get; private set; }

    /// <summary>Why the camera was last released, or null if it is currently owned.</summary>
    public string? LastReleaseReason { get; private set; }

    public void Take()
    {
        IsOwned = true;
        LastReleaseReason = null;
    }

    public void Release(string reason)
    {
        IsOwned = false;
        LastReleaseReason = reason;
    }
}
