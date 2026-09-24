using Vista.Core.Scenes;

namespace Vista.Core.Session;

/// <summary>The scene, the edited track and the selection, as one undo step restores them.</summary>
public readonly record struct EditSnapshot(Scene Scene, Guid Edited, SelectedItems Selection);

/// <summary>Undo and redo stacks of edit snapshots, keeping the most recent <see cref="Capacity"/> undo steps.</summary>
public sealed class EditHistory
{
    public const int Capacity = 100;

    private readonly LinkedList<EditSnapshot> undo = new();
    private readonly Stack<EditSnapshot> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Forgets every step.</summary>
    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }

    /// <summary>Records the state before a change and clears redo.</summary>
    public void Record(EditSnapshot before)
    {
        Push(before);
        redo.Clear();
    }

    /// <summary>The state to return to, keeping <paramref name="current"/> for redo; null when there is nothing to undo.</summary>
    public EditSnapshot? Undo(EditSnapshot current)
    {
        if (undo.Last is not { } last)
            return null;
        undo.RemoveLast();
        redo.Push(current);
        return last.Value;
    }

    /// <summary>The state to go forward to, keeping <paramref name="current"/> for undo; null when there is nothing to redo.</summary>
    public EditSnapshot? Redo(EditSnapshot current)
    {
        if (!redo.TryPop(out var next))
            return null;
        Push(current);
        return next;
    }

    private void Push(EditSnapshot snapshot)
    {
        undo.AddLast(snapshot);
        if (undo.Count > Capacity)
            undo.RemoveFirst();
    }
}
