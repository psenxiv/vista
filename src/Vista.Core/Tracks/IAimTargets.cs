using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>Finds characters in the game for a track to follow.</summary>
public interface IAimTargets
{
    /// <summary>The character named <paramref name="name"/>, on <paramref name="world"/> when given, nearest <paramref name="near"/>; null when none is loaded.</summary>
    LoadedCharacter? FindCharacter(string name, string? world, Vector3 near);
}
