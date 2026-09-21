namespace CinematicCam.Core;

/// <summary>A point on the timing curve: at this time, the camera is at this place on the path.</summary>
public sealed record TimingKey(float Time, float Position, TangentMode Mode, float InTangent, float OutTangent);
