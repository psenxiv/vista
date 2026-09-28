using Dalamud.Game.ClientState.Keys;
using Vista.Core.Input;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Editor;

/// <summary>Vista's once-per-press keys, read from physical key state, acted on and hidden from the game as Core resolves them.</summary>
internal sealed class EditorKeys
{
    private readonly record struct WatchedKey(Key Key, VirtualKey Virtual);

    private static readonly WatchedKey[] Watched = HotkeyResolver
        .Presses.Where(entry => entry != HotkeyTable.RestoreGameUi)
        .SelectMany(entry =>
            entry.Hotkey.Alternate is { } alternate ? new[] { entry.Hotkey.Key, alternate } : new[] { entry.Hotkey.Key }
        )
        .Distinct()
        .Select(key => new WatchedKey(key, HotkeyKeys.Virtual(key)))
        .ToArray();

    private readonly bool[] held = new bool[Watched.Length];

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(GameSession game, PointGizmo gizmo, EditorLayer layer)
    {
        if (PhysicalKeys.IsTyping())
        {
            Array.Clear(held);
            return;
        }

        var modifiers = HotkeyKeys.HeldModifiers();
        var context = HotkeyContext.Of(game.State);

        for (var i = 0; i < Watched.Length; i++)
        {
            var watched = Watched[i];
            var down = PhysicalKeys.IsDown(watched.Virtual);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down)
                continue;

            if (HotkeyResolver.Resolve(watched.Key, modifiers, context) is not { } press)
                continue;

            if (press.Hidden)
                PhysicalKeys.Hide(watched.Virtual);
            if (pressed && press.Acts)
                Act(game, gizmo, layer, press.Entry);
        }
    }

    private static void Act(GameSession game, PointGizmo gizmo, EditorLayer layer, HotkeyEntry entry)
    {
        var session = game.State;
        if (entry == HotkeyTable.Undo || entry == HotkeyTable.Redo)
        {
            // Nothing to undo or redo does nothing, as in any editor.
            _ = entry == HotkeyTable.Undo ? session.Undo() : session.Redo();
            return;
        }

        if (entry == HotkeyTable.Play)
            game.TogglePlay();
        else if (entry == HotkeyTable.Restart)
            game.RestartPlay();
        else if (entry == HotkeyTable.AddToEnd)
            Report(game.AddToEnd());
        else if (entry == HotkeyTable.AddAfterSelected)
            Report(game.AddAfterSelected());
        else if (entry == HotkeyTable.OverwriteSelected)
            Report(game.OverwriteSelected());
        else if (entry == HotkeyTable.LevelRoll)
            game.LevelCameraRoll();
        else if (entry == HotkeyTable.GizmoToggle)
            gizmo.Toggle();
        else if (entry == HotkeyTable.ColourByTurnSpeed)
            layer.Heat = !layer.Heat;
        else if (entry == HotkeyTable.DeleteSelectedPoints)
            Report(session.DeleteSelected());
    }
}
