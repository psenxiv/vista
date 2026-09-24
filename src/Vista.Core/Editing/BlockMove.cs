namespace Vista.Core.Editing;

/// <summary>Where rows dragged together land: as a block, in their order, beside the row they're dropped on.</summary>
public static class BlockMove
{
    /// <summary>The new order as old indices once <paramref name="moving"/>, grabbed by <paramref name="grabbed"/>, is dropped on <paramref name="target"/> (null for the end); null when nothing moves.</summary>
    /// <remarks>Dropped on a row below the grabbed one, the block goes just after it; above, just before it.</remarks>
    public static int[]? Order(int count, IReadOnlyCollection<int> moving, int grabbed, int? target)
    {
        if (moving.Count == 0 || moving.Any(i => i < 0 || i >= count))
            throw new ArgumentException("There is no such row to move.");
        if (!moving.Contains(grabbed))
            throw new ArgumentException("The grabbed row isn't one of those moving.");
        if (target is { } t && (t < 0 || t >= count))
            throw new ArgumentException("There is no such row to drop on.");
        if (target is { } inside && moving.Contains(inside))
            return null;

        var block = moving.Distinct().Order().ToArray();
        var rest = Enumerable.Range(0, count).Where(i => !block.Contains(i)).ToList();
        var at = target is { } row ? rest.IndexOf(row) + (row > grabbed ? 1 : 0) : rest.Count;
        rest.InsertRange(at, block);

        return rest.Select((old, slot) => old == slot).All(same => same) ? null : rest.ToArray();
    }

    /// <summary><paramref name="items"/> in <paramref name="order"/>, which must list each index once.</summary>
    public static IReadOnlyList<T> Apply<T>(IReadOnlyList<T> items, IReadOnlyList<int> order)
    {
        if (
            order.Count != items.Count
            || order.Distinct().Count() != items.Count
            || order.Any(i => i < 0 || i >= items.Count)
        )
            throw new ArgumentException("An order must list each row once.");
        return order.Select(i => items[i]).ToArray();
    }

    /// <summary>Where the row at <paramref name="old"/> went in <paramref name="order"/>.</summary>
    public static int NewIndex(IReadOnlyList<int> order, int old)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == old)
                return i;
        throw new ArgumentException("That row isn't in the order.");
    }
}
