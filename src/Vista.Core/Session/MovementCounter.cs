namespace Vista.Core.Session;

/// <summary>The game's movement lock counter, which Vista and other plugins share.</summary>
public static class MovementCounter
{
    /// <summary>The most holds the counter plausibly has at once; provisional.</summary>
    private const int MaxPlausible = 16;

    /// <summary>True when <paramref name="count"/> looks like the counter rather than a number a wrong signature match found.</summary>
    public static bool IsPlausible(int count) => count is >= 0 and <= MaxPlausible;

    /// <summary>True when a 4-byte counter at <paramref name="address"/> lies wholly inside the game's image, so reading it can't fault.</summary>
    public static bool InImage(long address, long imageStart, long imageSize) =>
        address >= imageStart && address <= imageStart + imageSize - sizeof(int);
}
