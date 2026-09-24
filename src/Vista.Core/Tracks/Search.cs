namespace Vista.Core.Tracks;

/// <summary>Binary search over an ascending array.</summary>
internal static class Search
{
    /// <summary>The index of the last entry at or below <paramref name="target"/>, searching between <paramref name="lo"/> and <paramref name="hi"/>; on return the next index is <paramref name="hi"/>'s side of it.</summary>
    public static int LastAtOrBelow(ReadOnlySpan<float> values, double target, int lo, int hi)
    {
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] <= target)
                lo = mid;
            else
                hi = mid;
        }

        return lo;
    }
}
