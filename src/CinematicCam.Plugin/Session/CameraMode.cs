namespace CinematicCam.Plugin;

/// <summary>What the plugin is doing with the camera. Exactly one at a time.</summary>
internal enum CameraMode
{
    /// <summary>The game has its camera; nothing is locked or blocked.</summary>
    Off,

    /// <summary>Free-cam drives the camera; the character is locked and flight keys and zoom are blocked.</summary>
    Editing,

    /// <summary>The Director drives the camera; the character is locked and flight keys and zoom are blocked.</summary>
    Live,
}
