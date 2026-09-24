namespace Vista.Core.Tracks.Aiming;

/// <summary>A Follow Target offset seen from the character: distance along the ground, angle round them (radians, 0 behind, a quarter turn to their right) and height above their feet.</summary>
public readonly record struct Orbit(float Distance, float Angle, float Height);
