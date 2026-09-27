using Vista.Core.Camera;

namespace Vista.Core.Session;

/// <summary>One editing frame: what the game camera shows, or null for the free-cam, and a frame to put the free-cam at first, or null.</summary>
public readonly record struct EditFrame(CameraState? Shown, CameraState? FlyFrom);
