namespace Vista.Core.Session;

/// <summary>The game's movement lock counter, which Vista and other plugins share.</summary>
public static class MovementCounter
{
    /// <summary>The most holds the counter plausibly has at once; provisional.</summary>
    private const int MaxPlausible = 16;

    /// <summary>True when <paramref name="count"/> looks like the counter rather than a number a wrong signature match found.</summary>
    public static bool IsPlausible(int count) => count is >= 0 and <= MaxPlausible;
}
