namespace Vista.Core.Display;

/// <summary>Fits a list row's name to its width: cut with an ellipsis at rest, scrolled while hovered.</summary>
public static class RowFit
{
    /// <summary>Seconds the scroll rests at each end.</summary>
    private const double Pause = 1.0;

    /// <summary>Pixels a second the scroll moves.</summary>
    private const float Speed = 30f;

    // Three full stops: the game font has no ellipsis glyph.
    private const string Mark = "...";

    /// <summary><paramref name="text"/> if it fits <paramref name="width"/>, else its longest start that fits with an ellipsis, as measured by <paramref name="measure"/>.</summary>
    public static string Ellipsis(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width)
            return text;

        // The longest start that fits: lo always fits, hi never does.
        var lo = 0;
        var hi = text.Length;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (measure(Cut(text, mid)) <= width)
                lo = mid;
            else
                hi = mid;
        }

        var cut = Cut(text, lo);
        return measure(cut) <= width ? cut : string.Empty;
    }

    /// <summary>How far, in pixels, a name <paramref name="overflow"/> pixels too wide is shifted left after <paramref name="seconds"/> of hover: rest, scroll to its end, rest, scroll back, repeat.</summary>
    public static float Scroll(float overflow, double seconds)
    {
        if (overflow <= 0f || seconds <= 0.0)
            return 0f;

        var travel = overflow / Speed;
        var t = seconds % ((2.0 * Pause) + (2.0 * travel));
        if (t < Pause)
            return 0f;
        if (t < Pause + travel)
            return (float)((t - Pause) * Speed);
        if (t < (2.0 * Pause) + travel)
            return overflow;
        return (float)(overflow - ((t - (2.0 * Pause) - travel) * Speed));
    }

    /// <summary>The first <paramref name="length"/> characters, never splitting a surrogate pair, without trailing spaces, and the ellipsis.</summary>
    private static string Cut(string text, int length)
    {
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length].TrimEnd() + Mark;
    }
}
