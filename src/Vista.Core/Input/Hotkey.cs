namespace Vista.Core.Input;

/// <summary>A key, its modifiers, and an alternate key that does the same thing.</summary>
public readonly record struct Hotkey(Key Key, Modifiers Modifiers = Modifiers.None, Key? Alternate = null)
{
    /// <summary>The name shown in menus and the Guide: modifiers first, then the key, joined by " + "; an alternate key reads "Delete / Backspace".</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(Modifiers.Ctrl))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(Modifiers.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(Modifiers.Shift))
                parts.Add("Shift");

            parts.Add(Alternate is { } alternate ? $"{Name(Key)} / {Name(alternate)}" : Name(Key));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>A tooltip naming the action and this hotkey, e.g. "Play (Space)".</summary>
    public string Tooltip(string action) => $"{action} ({DisplayName})";

    private static string Name(Key key) =>
        key switch
        {
            Key.Space => "Space",
            Key.Backtick => "Backtick",
            Key.Delete => "Delete",
            Key.Backspace => "Backspace",
            Key.Escape => "Escape",
            Key.Shift => "Shift",
            _ => key.ToString(),
        };
}
