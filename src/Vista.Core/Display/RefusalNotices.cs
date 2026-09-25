namespace Vista.Core.Display;

/// <summary>Which refusals to show the player: a message already shown within the last few seconds is not shown again.</summary>
public sealed class RefusalNotices
{
    /// <summary>How long after a message is shown the same message stays hidden, in seconds.</summary>
    public const double RepeatSeconds = 3.0;

    private readonly Dictionary<string, double> shown = new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="message"/> should be shown at <paramref name="now"/> seconds, which then counts as showing it.</summary>
    public bool Show(string message, double now)
    {
        foreach (var old in shown.Where(s => now - s.Value >= RepeatSeconds).Select(s => s.Key).ToList())
            shown.Remove(old);
        if (shown.ContainsKey(message))
            return false;
        shown[message] = now;
        return true;
    }
}
