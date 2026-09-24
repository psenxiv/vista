using System.Numerics;

namespace Vista.Core.Tracks.Aiming;

/// <summary>The characters loaded nearby as last read, found by exact name and world.</summary>
public sealed class NearbyCharacters
{
    private IReadOnlyList<LoadedCharacter> characters = [];

    /// <summary>Every character as last read.</summary>
    public IReadOnlyList<LoadedCharacter> All => characters;

    /// <summary>Replaces the characters with a copy of <paramref name="loaded"/>.</summary>
    public void Update(IReadOnlyList<LoadedCharacter> loaded) => characters = loaded.ToArray();

    /// <summary>The character named <paramref name="name"/>, on <paramref name="world"/> when given, nearest <paramref name="near"/>; null when none is loaded.</summary>
    public LoadedCharacter? FindCharacter(string name, string? world, Vector3 near)
    {
        LoadedCharacter? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var character in characters)
        {
            if (!string.Equals(character.Name, name, StringComparison.Ordinal))
                continue;
            if (world is not null && !string.Equals(character.World, world, StringComparison.Ordinal))
                continue;
            var distance = Vector3.DistanceSquared(character.Position, near);
            if (distance >= bestDistance)
                continue;
            best = character;
            bestDistance = distance;
        }

        return best;
    }
}
