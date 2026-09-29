namespace Vista.Core.Scenes;

/// <summary>A scene or preset file as the picker lists it; <paramref name="Tracks"/> is null for a preset, or a scene saved by a newer Vista.</summary>
public sealed record FileEntry(string Name, DateTime Modified, int? Tracks)
{
    /// <summary>The names of the scene files in <paramref name="entries"/> this Vista can open, which are those with a track count.</summary>
    public static IReadOnlyList<string> OpenableNames(IEnumerable<FileEntry> entries) =>
        [.. entries.Where(e => e.Tracks is not null).Select(e => e.Name)];
}
