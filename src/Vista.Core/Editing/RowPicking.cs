namespace Vista.Core.Editing;

/// <summary>How a list row was clicked: plain, with Ctrl, or with Shift.</summary>
public enum RowClick { Plain, Toggle, Range }

/// <summary>What a click on a list row does to the rows selected in that list.</summary>
public static class RowPicking
{
    /// <summary>Applies <paramref name="click"/> on <paramref name="clicked"/>; returns the selection in <paramref name="order"/>'s order and the row a later Shift-click ranges from.</summary>
    public static (IReadOnlyList<T> Selected, T? Last) Click<T>(IReadOnlyList<T> order, IReadOnlyCollection<T> selected, T? last, T clicked, RowClick click)
        where T : struct
    {
        var at = IndexOf(order, clicked);
        if (at < 0) throw new ArgumentException("There is no such row.");

        var picked = new HashSet<T>(selected);
        var from = last is { } l ? IndexOf(order, l) : -1;
        if (click == RowClick.Range && from < 0) click = RowClick.Toggle;

        switch (click)
        {
            case RowClick.Plain:
                return ([clicked], clicked);
            case RowClick.Toggle:
                if (!picked.Remove(clicked)) picked.Add(clicked);
                return (InOrder(order, picked), clicked);
            default:
                for (var i = Math.Min(from, at); i <= Math.Max(from, at); i++) picked.Add(order[i]);
                return (InOrder(order, picked), last);
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> order, T row)
    {
        for (var i = 0; i < order.Count; i++)
            if (EqualityComparer<T>.Default.Equals(order[i], row)) return i;
        return -1;
    }

    private static IReadOnlyList<T> InOrder<T>(IReadOnlyList<T> order, HashSet<T> picked) => order.Where(picked.Contains).ToArray();
}
