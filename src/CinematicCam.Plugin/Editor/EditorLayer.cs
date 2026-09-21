using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Game;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Editor;

/// <summary>Everything drawn over the game in editing mode: the overlay, marker clicks and the gizmo.</summary>
internal sealed class EditorLayer
{
    private const float HitRadius = Overlay.MarkerRadius + 4f;

    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

    private readonly CameraSession session;
    private readonly PointGizmo gizmo;
    private readonly Overlay overlay = new();
    private readonly ClickSelection clicks = new();

    public EditorLayer(CameraSession session, PointGizmo gizmo)
    {
        this.session = session;
        this.gizmo = gizmo;
    }

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (session.Mode != CameraMode.Editing) { clicks.Reset(); gizmo.Cancel(); return; }
        if (EditorView.Read() is not { } view) return;

        var track = gizmo.Preview is { } preview && preview.Index < session.Track.Points.Count
            ? TrackEditing.Replace(session.Track, preview.Index, preview.Point)
            : session.Track;
        var markers = overlay.Draw(view, track, session.Selected);
        var io = ImGui.GetIO();
        var hovered = MarkerHitTest.Nearest(markers, io.MousePos, HitRadius);

        // The window takes the mouse only over a marker, so those clicks never reach the game.
        var flags = BaseFlags;
        if (hovered is null && !clicks.HoldingMarker && !gizmo.Hot) flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(view.Origin);
        ImGui.SetNextWindowSize(view.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("##ccam-editor", flags))
        {
            ImGuizmo.BeginFrame();
            gizmo.Draw(view, session);

            // Dalamud hides presses from ImGui unless it wants the mouse, so read the button itself.
            var overUi = io.WantCaptureMouse && !ImGui.IsWindowHovered();
            var look = CameraAccess.ReadAngles() ?? (0f, 0f);
            Apply(clicks.Update(PhysicalKeys.IsDown(VirtualKey.LBUTTON), io.MousePos, look, overUi, gizmo.Hot, hovered));
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void Apply(ClickOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case ClickKind.Select:
                session.Select(outcome.Index);
                break;
            case ClickKind.Deselect:
                session.Select(null);
                break;
        }
    }
}
