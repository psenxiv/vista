using Vista.Core.Session;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace Vista.Plugin.Editor;

/// <summary>The editing-mode key bindings, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey[] Watched = [VirtualKey.C, VirtualKey.OEM_3, VirtualKey.Z, VirtualKey.Y, VirtualKey.R, VirtualKey.DELETE, VirtualKey.BACK];

    private readonly bool[] held = new bool[Watched.Length];

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session, PointGizmo gizmo)
    {
        if (session.Mode != CameraMode.Editing || PhysicalKeys.IsTyping()) { Array.Clear(held); return; }

        var ctrl = PhysicalKeys.IsDown(VirtualKey.CONTROL);
        var alt = PhysicalKeys.IsDown(VirtualKey.MENU);

        for (var i = 0; i < Watched.Length; i++)
        {
            var key = Watched[i];
            var down = PhysicalKeys.IsDown(key);
            var pressed = down && !held[i];
            held[i] = down;
            if (!down) continue;

            var deletes = key is VirtualKey.DELETE or VirtualKey.BACK && session.Selected is not null;
            var ours = key is VirtualKey.C or VirtualKey.OEM_3 or VirtualKey.R || ctrl || deletes;
            if (ours) PhysicalKeys.Hide(key);
            if (pressed && ours) Act(session, gizmo, key, ctrl, alt);
        }
    }

    private static void Act(CameraSession session, PointGizmo gizmo, VirtualKey key, bool ctrl, bool alt)
    {
        var refusal = key switch
        {
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

    private static string? Toggle(PointGizmo gizmo)
    {
        gizmo.Toggle();
        return null;
    }
}
