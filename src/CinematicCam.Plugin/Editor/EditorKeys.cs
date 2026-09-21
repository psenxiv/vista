using CinematicCam.Core.Session;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Game.ClientState.Keys;

namespace CinematicCam.Plugin.Editor;

/// <summary>The editing-mode key bindings, read from physical key state and hidden from the game.</summary>
internal sealed class EditorKeys
{
    /// <summary>Reads the keys and hides ours from the game. Call from Framework.Update.</summary>
    public void Update(CameraSession session)
    {
        if (session.Mode != CameraMode.Editing || PhysicalKeys.IsTyping()) return;

        if (PhysicalKeys.IsDown(VirtualKey.C)) PhysicalKeys.Hide(VirtualKey.C);
    }
}
