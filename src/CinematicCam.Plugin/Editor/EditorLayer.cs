using CinematicCam.Core.Session;
using CinematicCam.Plugin.Session;

namespace CinematicCam.Plugin.Editor;

/// <summary>Everything drawn over the game in editing mode: the overlay, marker clicks and the gizmo.</summary>
internal sealed class EditorLayer
{
    private readonly CameraSession session;
    private readonly Overlay overlay = new();

    public EditorLayer(CameraSession session) => this.session = session;

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (session.Mode != CameraMode.Editing) return;
        if (EditorView.Read() is not { } view) return;

        overlay.Draw(view, session.Track, session.Selected);
    }
}
