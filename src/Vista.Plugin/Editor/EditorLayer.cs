using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;

namespace Vista.Plugin.Editor;

/// <summary>Everything drawn over the game in editing mode: every shown track, marker clicks and the gizmo.</summary>
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

        var scene = session.Scene;
        var edited = session.EditedTrackId;
        var markers = new List<TrackMarker>();

        // Other tracks first, so the edited track draws on top.
        foreach (var other in scene.Tracks)
        {
            if (other.Id == edited || scene.Hidden.Contains(other.Id)) continue;
            AddMarkers(markers, other.Id, overlay.Draw(view, other, null, edited: false));
        }

        var track = gizmo.Preview is { } preview && preview.Index < session.Track.Points.Count
            ? TrackEditing.Replace(session.Track, preview.Index, preview.Point)
            : session.Track;
        AddMarkers(markers, edited, overlay.Draw(view, track, session.Selected, edited: true));
        overlay.Prune(scene.Tracks.Select(t => t.Id).ToHashSet());

        var io = ImGui.GetIO();
        var hovered = TrackMarkerHitTest.Nearest(markers, edited, io.MousePos, HitRadius);

        // The window takes the mouse only over a marker, so those clicks never reach the game.
        var flags = BaseFlags;
        if (hovered is null && !clicks.HoldingMarker && !gizmo.Hot) flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(view.Origin);
        ImGui.SetNextWindowSize(view.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("##vista-editor", flags))
        {
            ImGuizmo.BeginFrame();
            gizmo.Draw(view, session);

            // Dalamud hides presses from ImGui unless it wants the mouse, so read the button itself.
            var overUi = io.WantCaptureMouse && !ImGui.IsWindowHovered();
            var look = CameraAccess.ReadAngles() ?? (0f, 0f);
            Apply(clicks.Update(PhysicalKeys.IsDown(VirtualKey.LBUTTON), io.MousePos, look, overUi, gizmo.Hot, hovered), markers);
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>Selects a clicked point, switching to its track first when it isn't the edited one; a click on empty space clears the selection.</summary>
    private void Apply(ClickOutcome outcome, IReadOnlyList<TrackMarker> markers)
    {
        switch (outcome.Kind)
        {
            case ClickKind.Select when outcome.Index < markers.Count:
                var hit = markers[outcome.Index];
                if (hit.Track == session.EditedTrackId) session.Select(hit.Point);
                else Report(session.SelectPoint(hit.Track, hit.Point));
                break;
            case ClickKind.Deselect:
                session.Select(null);
                break;
        }
    }

    private static void AddMarkers(List<TrackMarker> markers, Guid track, IReadOnlyList<Vector2?> screens)
    {
        for (var i = 0; i < screens.Count; i++) markers.Add(new TrackMarker(track, i, screens[i]));
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[editor] {Refusal}", refusal);
    }
}
