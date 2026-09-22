using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A character loaded in the game: its name and where its feet are.</summary>
public readonly record struct LoadedCharacter(string Name, Vector3 Position);

/// <summary>The characters loaded nearby as last read, found by exact name.</summary>
public sealed class NearbyCharacters : IAimTargets
{
    private IReadOnlyList<LoadedCharacter> characters = [];

    /// <summary>Every character as last read.</summary>
    public IReadOnlyList<LoadedCharacter> All => characters;

    /// <summary>Replaces the characters with <paramref name="loaded"/>.</summary>
    public void Update(IReadOnlyList<LoadedCharacter> loaded) => characters = loaded;

    public Vector3? Find(string name, Vector3 near)
    {
        Vector3? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var character in characters)
        {
            if (!string.Equals(character.Name, name, StringComparison.Ordinal)) continue;
            var distance = Vector3.DistanceSquared(character.Position, near);
            if (distance >= bestDistance) continue;
            best = character.Position;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Every character, nearest <paramref name="place"/> first.</summary>
    public IReadOnlyList<LoadedCharacter> NearestTo(Vector3 place)
        => characters.OrderBy(c => Vector3.DistanceSquared(c.Position, place)).ToArray();
}
