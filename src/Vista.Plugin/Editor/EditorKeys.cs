using Dalamud.Game.ClientState.Keys;
using Vista.Core.Session;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Editor;

/// <summary>The key bindings for the modes Vista owns the camera in, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey[] Watched =
    [
        VirtualKey.SPACE,
        VirtualKey.OEM_3,
        VirtualKey.Z,
        VirtualKey.Y,
        VirtualKey.R,
        VirtualKey.DELETE,
        VirtualKey.BACK,
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

        var ctrl = PhysicalKeys.IsDown(VirtualKey.CONTROL);
        var alt = PhysicalKeys.IsDown(VirtualKey.MENU);

        for (var i = 0; i < Watched.Length; i++)
        {
            var key = Watched[i];
            var down = PhysicalKeys.IsDown(key);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down)
                continue;

            var deletes = key is VirtualKey.DELETE or VirtualKey.BACK && session.Selection.Points.Count > 0;
            var ours =
                key == VirtualKey.SPACE || (editing && (key is VirtualKey.OEM_3 or VirtualKey.R || ctrl || deletes));
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
            PhysicalKeys.IsDown(VirtualKey.CONTROL)
            || PhysicalKeys.IsDown(VirtualKey.SHIFT)
            || PhysicalKeys.IsDown(VirtualKey.MENU);
        var shown = session.OverlayShown;
        var down = shown && !modified && !PhysicalKeys.IsTyping() && PhysicalKeys.IsDown(VirtualKey.G);
        if (down && !heatHeld)
            layer.Heat = !layer.Heat;
        heatHeld = down;
        if (down && mode == CameraMode.Editing)
            PhysicalKeys.Hide(VirtualKey.G);
    }

    private static void Act(GameSession game, PointGizmo gizmo, VirtualKey key, bool ctrl, bool alt)
    {
        var session = game.State;
        // Nothing to undo or redo does nothing, as in any editor.
        if (key is VirtualKey.Z or VirtualKey.Y)
        {
            _ = key == VirtualKey.Z ? session.Undo() : session.Redo();
            return;
        }

        var refusal = key switch
        {
            VirtualKey.SPACE when ctrl => Restart(game),
            VirtualKey.SPACE => Transport(game),
            VirtualKey.OEM_3 when ctrl && alt => null,
            VirtualKey.OEM_3 when ctrl => game.OverwriteSelected(),
            VirtualKey.OEM_3 when alt => game.AddAfterSelected(),
            VirtualKey.OEM_3 => game.AddToEnd(),
            VirtualKey.R
                when session.Selection.Point is not null
                    || session.Selection.Anchor is AnchorKind.Scene or AnchorKind.Track => Toggle(gizmo),
            VirtualKey.DELETE or VirtualKey.BACK => session.DeleteSelected(),
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

    private static string? Toggle(PointGizmo gizmo)
    {
        gizmo.Toggle();
        return null;
    }
}
