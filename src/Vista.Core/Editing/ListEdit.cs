namespace Vista.Core.Editing;

/// <summary>Finding, replacing and inserting items in a read-only list, as a new list.</summary>
public static class ListEdit
{
    /// <summary>The index of the first item <paramref name="match"/> accepts, or −1.</summary>
    public static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Count; i++)
            if (match(items[i]))
                return i;
        return -1;
    }

    /// <summary>A copy of <paramref name="items"/> with the item at <paramref name="index"/> replaced by <paramref name="item"/>.</summary>
    public static T[] Replace<T>(IReadOnlyList<T> items, int index, T item)
    {
        var copy = items.ToArray();
        copy[index] = item;
        return copy;
    }

    /// <summary>A copy of <paramref name="items"/> with <paramref name="item"/> inserted at <paramref name="index"/>, which may be the end.</summary>
    public static T[] Insert<T>(IReadOnlyList<T> items, int index, T item) => InsertRange(items, index, [item]);

    /// <summary>A copy of <paramref name="items"/> with <paramref name="inserted"/> inserted in order at <paramref name="index"/>, which may be the end.</summary>
    public static T[] InsertRange<T>(IReadOnlyList<T> items, int index, IEnumerable<T> inserted)
    {
        var copy = items.ToList();
        copy.InsertRange(index, inserted);
        return [.. copy];
    }
}
