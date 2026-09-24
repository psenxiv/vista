namespace Vista.Core.Scenes;

/// <summary>Checks and suggests scene and preset names, which are also their file names.</summary>
public static class SceneNames
{
    /// <summary>The longest name, in characters, after trimming.</summary>
    public const int MaxLength = 64;

    private const string Unusable = "That name can't be used as a file name";

    private static readonly HashSet<char> Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> Devices = new(
        [
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Why the trimmed <paramref name="name"/> can't be a file name, or null when it can.</summary>
    public static string? Refusal(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Enter a name";
        if (trimmed.Length > MaxLength)
            return "That name is too long";
        if (trimmed.Any(c => char.IsControl(c) || Forbidden.Contains(c)))
            return Unusable;
        if (trimmed.EndsWith('.') || Devices.Contains(trimmed))
            return Unusable;
        return null;
    }

    /// <summary>True when a name in <paramref name="existing"/> matches the trimmed <paramref name="name"/>, ignoring case.</summary>
    public static bool Taken(string name, IEnumerable<string> existing)
    {
        var trimmed = name.Trim();
        return existing.Any(e => string.Equals(e, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The first of "stem 1", "stem 2", … that is not taken.</summary>
    public static string NextFree(string stem, IEnumerable<string> existing)
    {
        var names = existing.ToList();
        for (var n = 1; ; n++)
        {
            var candidate = $"{stem} {n}";
            if (!Taken(candidate, names))
                return candidate;
        }
    }

    /// <summary>"name copy", then "name copy 2", "name copy 3", …, the first not taken.</summary>
    public static string CopyOf(string name, IEnumerable<string> existing)
    {
        var names = existing.ToList();
        var copy = $"{name} copy";
        if (!Taken(copy, names))
            return copy;
        for (var n = 2; ; n++)
        {
            var candidate = $"{copy} {n}";
            if (!Taken(candidate, names))
                return candidate;
        }
    }
}
