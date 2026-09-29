namespace Vista.Core.Scenes;

/// <summary>Filtering a listing for the picker by name.</summary>
public static class FileList
{
    /// <summary><paramref name="items"/> whose <paramref name="name"/> contains the trimmed <paramref name="search"/> anywhere, case-insensitive, ordered by name ignoring case.</summary>
    public static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> items, string search, Func<T, string> name)
    {
        var trimmed = search.Trim();
        return items
            .Where(i => trimmed.Length == 0 || name(i).Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
