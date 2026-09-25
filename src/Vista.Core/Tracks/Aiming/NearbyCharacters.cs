using System.Numerics;

namespace Vista.Core.Tracks.Aiming;

/// <summary>The characters loaded nearby as last read, found by exact name and world.</summary>
public sealed class NearbyCharacters
{
    private IReadOnlyList<LoadedCharacter> characters = [];

    /// <summary>Every character as last read.</summary>
    public IReadOnlyList<LoadedCharacter> All => characters;

    /// <summary>A character's name and home world as a list shows it, or NPC with no world.</summary>
    public static string Label(string name, string? world) => $"{name} · {world ?? "NPC"}";

    /// <summary>The characters whose names contain the trimmed <paramref name="search"/>, ignoring case, once per name and world, by name then world.</summary>
    public IReadOnlyList<LoadedCharacter> Listed(string search)
    {
        var filter = search.Trim();
        return characters
            .Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => (c.Name, c.World))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.World ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
