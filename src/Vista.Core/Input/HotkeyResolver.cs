using System.Numerics;
using Vista.Core.Session;

namespace Vista.Core.Input;

/// <summary>Which hotkey a key press, or the flight keys held, resolve to.</summary>
public static class HotkeyResolver
{
    /// <summary>The hotkeys that act once per press, in the order a press is matched.</summary>
    public static readonly IReadOnlyList<HotkeyEntry> Presses =
    [
        HotkeyTable.Play,
        HotkeyTable.Restart,
        HotkeyTable.AddToEnd,
        HotkeyTable.AddAfterSelected,
        HotkeyTable.OverwriteSelected,
        HotkeyTable.LevelRoll,
        HotkeyTable.GizmoToggle,
        HotkeyTable.ColourByTurnSpeed,
        HotkeyTable.DeleteSelectedPoints,
        HotkeyTable.Undo,
        HotkeyTable.Redo,
        HotkeyTable.RestoreGameUi,
    ];

    /// <summary>The press <paramref name="key"/> with <paramref name="held"/> is in <paramref name="context"/>, or null when it isn't Vista's; Ctrl and Alt must match the hotkey's, Shift never matters.</summary>
    public static HotkeyPress? Resolve(Key key, Modifiers held, HotkeyContext context)
    {
        var modifiers = held & ~Modifiers.Shift;
        foreach (var entry in Presses)
        {
            var hotkey = entry.Hotkey;
            if ((hotkey.Key == key || hotkey.Alternate == key) && hotkey.Modifiers == modifiers)
                return In(entry, context);
        }

        return null;
    }

    /// <summary>The flight asked for by the keys <paramref name="down"/> says are held, with <paramref name="held"/>: Ctrl turns the up and down keys into roll.</summary>
    public static FlightKeys Flight(Func<Key, bool> down, Modifiers held)
    {
        float Axis(HotkeyEntry plus, HotkeyEntry minus) =>
            (down(plus.Hotkey.Key) ? 1f : 0f) - (down(minus.Hotkey.Key) ? 1f : 0f);

        var rolling = held.HasFlag(HotkeyTable.RollRight.Hotkey.Modifiers);
        var forward = Axis(HotkeyTable.FlyForward, HotkeyTable.FlyBack);
        var right = Axis(HotkeyTable.FlyRight, HotkeyTable.FlyLeft);
        var up = rolling ? 0f : Axis(HotkeyTable.FlyUp, HotkeyTable.FlyDown);
        var roll = rolling ? Axis(HotkeyTable.RollRight, HotkeyTable.RollLeft) : 0f;
        return new FlightKeys(new Vector3(forward, up, right), roll, down(HotkeyTable.FlyFaster.Hotkey.Key));
    }

    private static HotkeyPress? In(HotkeyEntry entry, HotkeyContext context)
    {
        var editing = context.Mode == CameraMode.Editing;
        var owned = editing || context.Mode == CameraMode.Live;
        return entry switch
        {
            _ when entry == HotkeyTable.Play || entry == HotkeyTable.Restart => owned ? new(entry, true, true) : null,
            _ when entry == HotkeyTable.GizmoToggle => editing ? new(entry, context.GizmoTarget, true) : null,
            _ when entry == HotkeyTable.ColourByTurnSpeed => context.OverlayShown ? new(entry, true, editing) : null,
            _ when entry == HotkeyTable.DeleteSelectedPoints => editing && context.PointsSelected
                ? new(entry, true, true)
                : null,
            _ when entry == HotkeyTable.RestoreGameUi => context.Mode == CameraMode.Live
                ? new(entry, true, false)
                : null,
            _ => editing ? new(entry, true, true) : null,
        };
    }
}
