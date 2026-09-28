namespace Vista.Core.Scenes;

/// <summary>Filtering a file listing for the picker.</summary>
public static class FileList
{
    /// <summary><paramref name="entries"/> whose name contains the trimmed <paramref name="search"/> anywhere, case-insensitive, ordered by name ignoring case.</summary>
    public static IReadOnlyList<FileEntry> Filter(IReadOnlyList<FileEntry> entries, string search)
    {
        var trimmed = search.Trim();
        return entries
            .Where(e => trimmed.Length == 0 || e.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
