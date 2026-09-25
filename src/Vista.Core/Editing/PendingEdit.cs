using System.Numerics;

namespace Vista.Core.Editing;

/// <summary>A field's changed value, held while the field is held and applied once it's let go, so each change is one undo step.</summary>
public sealed class PendingEdit<T>(Func<bool> canApply)
    where T : IEqualityOperators<T, T, bool>
{
    private (string Id, T Value, T Shown, Action<T> Apply)? pending;

    /// <summary>The field holding a value, or null.</summary>
    public string? HeldBy => pending?.Id;

    /// <summary>What field <paramref name="id"/> shows: its held value, or <paramref name="current"/>.</summary>
    public T ValueOr(string id, T current) => pending is { } p && p.Id == id ? p.Value : current;

    /// <summary>Holds <paramref name="value"/> for field <paramref name="id"/>, which showed <paramref name="current"/>, in place of any value already held.</summary>
    public void Hold(string id, T value, T current, Action<T> apply) => pending = (id, value, current, apply);

    /// <summary>Applies the held value; dropped if it can no longer apply or did not change.</summary>
    public void Commit()
    {
        if (pending is not { } p)
            return;
        pending = null;
        if (canApply() && p.Value != p.Shown)
            p.Apply(p.Value);
    }

    /// <summary>Applies field <paramref name="id"/>'s held value, if it holds one.</summary>
    public void Commit(string id)
    {
        if (pending is { } p && p.Id == id)
            Commit();
    }

    /// <summary>Drops the held value without applying it.</summary>
    public void Clear() => pending = null;
}
