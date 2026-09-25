namespace Vista.Core.Editing;

/// <summary>What a click on a list row does to the rows selected in that list.</summary>
public static class RowPicking
{
    /// <summary>The kind of click the held modifiers make: Shift wins over Ctrl.</summary>
    public static RowClick FromKeys(bool shift, bool ctrl) =>
        shift ? RowClick.Range
        : ctrl ? RowClick.Toggle
        : RowClick.Plain;

    /// <summary>Applies <paramref name="click"/> on <paramref name="clicked"/>; returns the selection in <paramref name="order"/>'s order and the row a later Shift-click ranges from.</summary>
    public static (IReadOnlyList<T> Selected, T? Last) Click<T>(
        IReadOnlyList<T> order,
        IReadOnlyCollection<T> selected,
        T? last,
        T clicked,
        RowClick click
    )
        where T : struct
    {
        var at = IndexOf(order, clicked);
        if (at < 0)
            throw new ArgumentException("There is no such row.");

        var picked = new HashSet<T>(selected);
        var from = last is { } l ? IndexOf(order, l) : -1;
        if (click == RowClick.Range && from < 0)
            click = RowClick.Toggle;

        switch (click)
        {
            case RowClick.Plain:
                return ([clicked], clicked);
            case RowClick.Toggle:
                if (!picked.Remove(clicked))
                    picked.Add(clicked);
                return (InOrder(order, picked), clicked);
            default:
                for (var i = Math.Min(from, at); i <= Math.Max(from, at); i++)
                    picked.Add(order[i]);
                return (InOrder(order, picked), last);
        }
    }

    /// <summary>True when <paramref name="row"/> is one of two or more selected, so dragging it or its menu acts on them all.</summary>
    public static bool IsGroup<T>(IReadOnlyCollection<T> selected, T row) =>
        selected.Contains(row) && selected.Count >= 2;

    /// <summary>The rows a drag carries: the selection for a group, else the grabbed row while it is still in the list.</summary>
    public static IReadOnlyList<T> Carried<T>(
        IReadOnlyList<T> rows,
        IReadOnlyList<T> selected,
        int grabbed,
        bool group
    ) =>
        group ? selected
        : grabbed < rows.Count ? [rows[grabbed]]
        : [];

    private static int IndexOf<T>(IReadOnlyList<T> order, T row) =>
        ListEdit.IndexOf(order, r => EqualityComparer<T>.Default.Equals(r, row));

    private static T[] InOrder<T>(IReadOnlyList<T> order, HashSet<T> picked) => order.Where(picked.Contains).ToArray();
}
