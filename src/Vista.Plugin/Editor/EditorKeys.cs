using Vista.Core.Session;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace Vista.Plugin.Editor;

/// <summary>The key bindings for the modes Vista owns the camera in, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey[] Watched = [VirtualKey.SPACE, VirtualKey.OEM_3, VirtualKey.Z, VirtualKey.Y, VirtualKey.R, VirtualKey.DELETE, VirtualKey.BACK];

    private readonly bool[] held = new bool[Watched.Length];
    private bool heatHeld;

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session, PointGizmo gizmo, EditorLayer layer)
    {
        ToggleHeat(session, layer);
        if (session.Released || PhysicalKeys.IsTyping()) { Array.Clear(held); return; }

        var editing = session.Mode == CameraMode.Editing;

        var ctrl = PhysicalKeys.IsDown(VirtualKey.CONTROL);
        var alt = PhysicalKeys.IsDown(VirtualKey.MENU);

        for (var i = 0; i < Watched.Length; i++)
        {
            var key = Watched[i];
            var down = PhysicalKeys.IsDown(key);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down) continue;

            var deletes = key is VirtualKey.DELETE or VirtualKey.BACK && session.SelectedPoints.Count > 0;
            var ours = key == VirtualKey.SPACE || (editing && (key is VirtualKey.OEM_3 or VirtualKey.R || ctrl || deletes));
            if (ours) PhysicalKeys.Hide(key);
            if (pressed && ours) Act(session, gizmo, key, ctrl, alt);
        }
    }

    /// <summary>G alone toggles turn heat in Edit, not while previewing, and in View; View leaves the key to the game too, since the game has the camera there.</summary>
    private void ToggleHeat(CameraSession session, EditorLayer layer)
    {
        var mode = session.Mode;
        var modified = PhysicalKeys.IsDown(VirtualKey.CONTROL) || PhysicalKeys.IsDown(VirtualKey.SHIFT) || PhysicalKeys.IsDown(VirtualKey.MENU);
        var shown = mode == CameraMode.View || (mode == CameraMode.Editing && !session.Previewing);
        var down = shown && !modified && !PhysicalKeys.IsTyping() && PhysicalKeys.IsDown(VirtualKey.G);
        if (down && !heatHeld) layer.Heat = !layer.Heat;
        heatHeld = down;
        if (down && mode == CameraMode.Editing) PhysicalKeys.Hide(VirtualKey.G);
    }

    private static void Act(CameraSession session, PointGizmo gizmo, VirtualKey key, bool ctrl, bool alt)
    {
        var refusal = key switch
        {
            VirtualKey.SPACE when ctrl => Restart(session),
            VirtualKey.SPACE => Transport(session),
            VirtualKey.OEM_3 when ctrl && alt => null,
            VirtualKey.OEM_3 when ctrl => session.OverwriteSelected(),
            VirtualKey.OEM_3 when alt => session.AddAfterSelected(),
            VirtualKey.OEM_3 => session.AddToEnd(),
            VirtualKey.Z => session.Undo() ? null : "Nothing to undo.",
            VirtualKey.Y => session.Redo() ? null : "Nothing to redo.",
            VirtualKey.R when session.Selected is not null || session.SelectedAnchor is AnchorKind.Scene or AnchorKind.Track => Toggle(gizmo),
            VirtualKey.DELETE or VirtualKey.BACK => session.DeleteSelected(),
            _ => null,
        };

        if (refusal is not null) Plugin.Log.Debug("[editor] {Key}: {Refusal}", key.ToString(), refusal);
    }

    /// <summary>Space does what the Play button would: pauses a running shot, starts one otherwise.</summary>
    private static string? Transport(CameraSession session)
    {
        if (session.IsPlaying) session.StopPlay();
        else session.StartPlay();
        return null;
    }

    private static string? Restart(CameraSession session)
    {
        session.RestartPlay();
        return null;
    }

    private static string? Toggle(PointGizmo gizmo)
    {
        gizmo.Toggle();
        return null;
    }
}
