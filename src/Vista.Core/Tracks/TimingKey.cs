namespace CinematicCam.Core.Tracks;

/// <summary>A point on the timing curve: at this time the camera is at this place on the path, with a tangent mode on each side.</summary>
public sealed record TimingKey(
    float Time,
    float Position,
    TangentMode InMode = TangentMode.Auto,
    TangentMode OutMode = TangentMode.Auto,
    float InTangent = 0f,
    float OutTangent = 0f,
    bool Broken = false);
