using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>Finds characters in the game for a track to follow.</summary>
public interface IAimTargets
{
    /// <summary>The feet of the character named <paramref name="name"/> nearest <paramref name="near"/>, or null when none is loaded.</summary>
    Vector3? Find(string name, Vector3 near);
}
