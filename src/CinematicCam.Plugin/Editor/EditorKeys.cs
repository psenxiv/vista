using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Editor;

/// <summary>The editing-mode key bindings, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    private static readonly VirtualKey[] Watched = [VirtualKey.C, VirtualKey.OEM_3, VirtualKey.Z, VirtualKey.Y];

    private readonly bool[] held = new bool[Watched.Length];

    /// <summary>Reads the keys, acts on new presses and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session)
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

            var ours = key is VirtualKey.C or VirtualKey.OEM_3 || ctrl;
            if (ours) PhysicalKeys.Hide(key);
            if (pressed && ours) Act(session, key, ctrl, alt);
        }
    }

    private static void Act(CameraSession session, VirtualKey key, bool ctrl, bool alt)
    {
        var refusal = key switch
        {
            VirtualKey.OEM_3 when ctrl => session.OverwriteSelected(),
            VirtualKey.OEM_3 when alt => session.AddAfterSelected(),
            VirtualKey.OEM_3 => session.AddToEnd(),
            VirtualKey.Z => session.Undo() ? null : "Nothing to undo.",
            VirtualKey.Y => session.Redo() ? null : "Nothing to redo.",
            _ => null,
        };

        if (refusal is not null) Plugin.Log.Debug("[editor] {Key}: {Refusal}", key.ToString(), refusal);
    }
}
