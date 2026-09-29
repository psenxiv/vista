namespace Vista.Core.Scenes;

/// <summary>What to tell the player about scene files that can't be read, once per file.</summary>
public sealed class UnreadableNotices
{
    private readonly HashSet<string> told = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The notice that scene file <paramref name="name"/> is left out of the list, or null when the player was already told about it.</summary>
    public string? Unlisted(string name) =>
        told.Add(name) ? $"Could not read {name}, so it isn't listed. The file may be damaged." : null;

    /// <summary>The notice that scene file <paramref name="name"/> can't be read and <paramref name="opened"/> is open instead, which counts as telling the player about the file.</summary>
    public string Replaced(string name, string opened)
    {
        told.Add(name);
        return $"Could not read {name}, so {opened} is open instead. The file may be damaged.";
    }
}
