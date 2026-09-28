namespace Vista.Core.Input;

/// <summary>Every hotkey Vista binds, named once for key handling, menus, tooltips and the Guide to read.</summary>
public static class HotkeyTable
{
    public static readonly HotkeyEntry FlyForward = new(nameof(FlyForward), new Hotkey(Key.W));
    public static readonly HotkeyEntry FlyLeft = new(nameof(FlyLeft), new Hotkey(Key.A));
    public static readonly HotkeyEntry FlyBack = new(nameof(FlyBack), new Hotkey(Key.S));
    public static readonly HotkeyEntry FlyRight = new(nameof(FlyRight), new Hotkey(Key.D));
    public static readonly HotkeyEntry FlyUp = new(nameof(FlyUp), new Hotkey(Key.E));
    public static readonly HotkeyEntry FlyDown = new(nameof(FlyDown), new Hotkey(Key.Q));
    public static readonly HotkeyEntry RollLeft = new(nameof(RollLeft), new Hotkey(Key.Q, Modifiers.Ctrl));
    public static readonly HotkeyEntry RollRight = new(nameof(RollRight), new Hotkey(Key.E, Modifiers.Ctrl));
    public static readonly HotkeyEntry LevelRoll = new(nameof(LevelRoll), new Hotkey(Key.R, Modifiers.Alt));
    public static readonly HotkeyEntry FlyFaster = new(nameof(FlyFaster), new Hotkey(Key.Shift));
    public static readonly HotkeyEntry Play = new(nameof(Play), new Hotkey(Key.Space));
    public static readonly HotkeyEntry Restart = new(nameof(Restart), new Hotkey(Key.Space, Modifiers.Ctrl));
    public static readonly HotkeyEntry AddToEnd = new(nameof(AddToEnd), new Hotkey(Key.Backtick));
    public static readonly HotkeyEntry AddAfterSelected = new(
        nameof(AddAfterSelected),
        new Hotkey(Key.Backtick, Modifiers.Alt)
    );
    public static readonly HotkeyEntry OverwriteSelected = new(
        nameof(OverwriteSelected),
        new Hotkey(Key.Backtick, Modifiers.Ctrl)
    );
    public static readonly HotkeyEntry GizmoToggle = new(nameof(GizmoToggle), new Hotkey(Key.R));
    public static readonly HotkeyEntry ColourByTurnSpeed = new(nameof(ColourByTurnSpeed), new Hotkey(Key.G));
    public static readonly HotkeyEntry DeleteSelectedPoints = new(
        nameof(DeleteSelectedPoints),
        new Hotkey(Key.Delete, Alternate: Key.Backspace)
    );
    public static readonly HotkeyEntry Undo = new(nameof(Undo), new Hotkey(Key.Z, Modifiers.Ctrl));
    public static readonly HotkeyEntry Redo = new(nameof(Redo), new Hotkey(Key.Y, Modifiers.Ctrl));

    // Escape while Live brings back the game UI Vista hid; Plugin.cs watches for it like any other bound key.
    public static readonly HotkeyEntry RestoreGameUi = new(nameof(RestoreGameUi), new Hotkey(Key.Escape));

    /// <summary>Every hotkey, for the Guide's coverage test and this table's name lookup.</summary>
    public static readonly IReadOnlyList<HotkeyEntry> All =
    [
        FlyForward,
        FlyLeft,
        FlyBack,
        FlyRight,
        FlyUp,
        FlyDown,
        RollLeft,
        RollRight,
        LevelRoll,
        FlyFaster,
        Play,
        Restart,
        AddToEnd,
        AddAfterSelected,
        OverwriteSelected,
        GizmoToggle,
        ColourByTurnSpeed,
        DeleteSelectedPoints,
        Undo,
        Redo,
        RestoreGameUi,
    ];

    private static readonly IReadOnlyDictionary<string, HotkeyEntry> ByName = All.ToDictionary(
        entry => entry.Name,
        StringComparer.Ordinal
    );

    /// <summary>The entry named <paramref name="name"/>, case-sensitive, or null when there isn't one.</summary>
    public static HotkeyEntry? Find(string name) => ByName.GetValueOrDefault(name);
}
