using Dalamud.Game.ClientState.Keys;
using Vista.Core.Input;
using Vista.Core.Session;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Editor;

/// <summary>The key bindings for the modes Vista owns the camera in, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey SpaceKey = HotkeyKeys.Virtual(HotkeyTable.Play.Hotkey.Key);
    private static readonly VirtualKey BacktickKey = HotkeyKeys.Virtual(HotkeyTable.AddToEnd.Hotkey.Key);
    private static readonly VirtualKey UndoKey = HotkeyKeys.Virtual(HotkeyTable.Undo.Hotkey.Key);
    private static readonly VirtualKey RedoKey = HotkeyKeys.Virtual(HotkeyTable.Redo.Hotkey.Key);

    // Also LevelRoll's key (Alt + R); GizmoToggle names it since both share VirtualKey R.
    private static readonly VirtualKey RKey = HotkeyKeys.Virtual(HotkeyTable.GizmoToggle.Hotkey.Key);
    private static readonly VirtualKey DeleteKey = HotkeyKeys.Virtual(HotkeyTable.DeleteSelectedPoints.Hotkey.Key);
    private static readonly VirtualKey BackspaceKey = HotkeyKeys.Virtual(
        HotkeyTable.DeleteSelectedPoints.Hotkey.Alternate!.Value
    );
    private static readonly VirtualKey HeatKey = HotkeyKeys.Virtual(HotkeyTable.ColourByTurnSpeed.Hotkey.Key);

    private static readonly VirtualKey[] Watched =
    [
        SpaceKey,
        BacktickKey,
        UndoKey,
        RedoKey,
        RKey,
        DeleteKey,
        BackspaceKey,
    ];

    private readonly bool[] held = new bool[Watched.Length];
    private bool heatHeld;

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(GameSession game, PointGizmo gizmo, EditorLayer layer)
    {
        var session = game.State;
        ToggleHeat(session, layer);
        if (session.Released || PhysicalKeys.IsTyping())
        {
            Array.Clear(held);
            return;
        }

        var editing = session.Mode == CameraMode.Editing;

        var ctrl = PhysicalKeys.IsDown(HotkeyKeys.Ctrl);
        var alt = PhysicalKeys.IsDown(HotkeyKeys.Alt);

        for (var i = 0; i < Watched.Length; i++)
        {
            var key = Watched[i];
            var down = PhysicalKeys.IsDown(key);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down)
                continue;

            var deletes = (key == DeleteKey || key == BackspaceKey) && session.Selection.Points.Count > 0;
            var ours = key == SpaceKey || (editing && (key == BacktickKey || key == RKey || ctrl || deletes));
            if (ours)
                PhysicalKeys.Hide(key);
            if (pressed && ours)
                Act(game, gizmo, key, ctrl, alt);
        }
    }

    /// <summary>G alone toggles turn heat in Edit, not while previewing, and in View; View leaves the key to the game too, since the game has the camera there.</summary>
    private void ToggleHeat(SessionState session, EditorLayer layer)
    {
        var mode = session.Mode;
        var modified =
            PhysicalKeys.IsDown(HotkeyKeys.Ctrl)
            || PhysicalKeys.IsDown(HotkeyKeys.Shift)
            || PhysicalKeys.IsDown(HotkeyKeys.Alt);
        var shown = session.OverlayShown;
        var down = shown && !modified && !PhysicalKeys.IsTyping() && PhysicalKeys.IsDown(HeatKey);
        if (down && !heatHeld)
            layer.Heat = !layer.Heat;
        heatHeld = down;
        if (down && mode == CameraMode.Editing)
            PhysicalKeys.Hide(HeatKey);
    }

    private static void Act(GameSession game, PointGizmo gizmo, VirtualKey key, bool ctrl, bool alt)
    {
        var session = game.State;
        // Nothing to undo or redo does nothing, as in any editor.
        if (key == UndoKey || key == RedoKey)
        {
            _ = key == UndoKey ? session.Undo() : session.Redo();
            return;
        }

        var refusal = key switch
        {
            _ when key == SpaceKey && ctrl => Restart(game),
            _ when key == SpaceKey => Transport(game),
            _ when key == BacktickKey && ctrl && alt => null,
            _ when key == BacktickKey && ctrl => game.OverwriteSelected(),
            _ when key == BacktickKey && alt => game.AddAfterSelected(),
            _ when key == BacktickKey => game.AddToEnd(),
            _ when key == RKey && alt && !ctrl => Level(game),
            _ when key == RKey
                    && (
                        session.Selection.Point is not null
                        || session.Selection.Anchor is AnchorKind.Scene or AnchorKind.Track
                    ) => Toggle(gizmo),
            _ when key == DeleteKey || key == BackspaceKey => session.DeleteSelected(),
            _ => null,
        };

        Report(refusal);
    }

    /// <summary>Space does what the Play button would: pauses a running shot, starts one otherwise.</summary>
    private static string? Transport(GameSession game)
    {
        game.TogglePlay();
        return null;
    }

    private static string? Restart(GameSession game)
    {
        game.RestartPlay();
        return null;
    }

    private static string? Level(GameSession game)
    {
        game.LevelCameraRoll();
        return null;
    }

    private static string? Toggle(PointGizmo gizmo)
    {
        gizmo.Toggle();
        return null;
    }
}
