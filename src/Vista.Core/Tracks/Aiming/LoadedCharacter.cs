using System.Numerics;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A character loaded in the game: its name, a player's home world or null for an NPC, where its feet are and its facing as a camera yaw.</summary>
public readonly record struct LoadedCharacter(string Name, string? World, Vector3 Position, float Facing = 0f);
