using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;
using Vista.Plugin.Session;

namespace Vista.Plugin.Editor;

/// <summary>Everything drawn over the game in Edit and View: every shown track, and in Edit marker clicks and the gizmo.</summary>
internal sealed class EditorLayer
{
    private const float HitRadius = Overlay.MarkerRadius + 4f;

    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoSavedSettings;

    private readonly GameSession game;
    private readonly SessionState session;
    private readonly PointGizmo gizmo;
    private readonly AnchorGizmo anchorGizmo;
    private readonly Overlay overlay = new();
    private readonly ClickSelection clicks = new();

    /// <summary>True while the edited track's path is drawn by how fast its camera turns.</summary>
    public bool Heat { get; set; }

    public EditorLayer(GameSession game, PointGizmo gizmo)
    {
        this.game = game;
        session = game.State;
        this.gizmo = gizmo;
        anchorGizmo = new AnchorGizmo(gizmo);
    }

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        var editing = session.OverlayEditable;
        if (!editing)
        {
            clicks.Reset();
            gizmo.Cancel();
            anchorGizmo.Cancel(session);
        }
        if (!session.OverlayShown)
            return;
        if (EditorView.Read() is not { } view)
            return;
        var selectedAnchor = editing ? session.Selection.Anchor : null;

        var scene = session.Scene;
        var edited = session.EditedTrackId;
        var markers = new List<TrackMarker>();

        // Other tracks first, so the edited track draws on top.
        foreach (var other in scene.Tracks)
        {
            if (other.Id == edited || scene.Hidden.Contains(other.Id))
                continue;
            var otherWorld = session.World.Shown(other);
            AddMarkers(
                markers,
                other.Id,
                overlay.Draw(view, otherWorld, [], edited: false, session.World.AimPoint(otherWorld))
            );
            if (session.World.TargetPoint(otherWorld) is { } otherTarget)
                Overlay.DrawTargetMarker(view, otherTarget, FirstPosition(otherWorld), edited: false);
            DrawAnchors(view, scene, other, otherWorld, edited: false, selected: null, markers);
        }

        var track =
            editing && gizmo.Preview is { } preview && preview.Index < session.Track.Points.Count
                ? TrackEditing.Replace(session.Track, preview.Index, preview.Point)
                : session.Track;
        AddMarkers(
            markers,
            edited,
            overlay.Draw(
                view,
                track,
                editing ? session.Selection.Points : [],
                edited: true,
                session.World.AimPoint(track),
                Heat
            )
        );
        overlay.Prune(scene.Tracks.Select(t => t.Id).ToHashSet());

        var editedLocal = SceneEditing.Get(scene, edited);
        DrawAnchors(view, scene, editedLocal, track, edited: true, selectedAnchor, markers);
        if (session.World.TargetPoint(track) is { } target)
            Overlay.DrawTargetMarker(view, target, FirstPosition(track), edited: true);
        if (scene.AnchorPlaced)
            markers.Add(
                new TrackMarker(
                    Guid.Empty,
                    -1,
                    Overlay.DrawSceneAnchor(view, scene.Anchor, selectedAnchor == AnchorKind.Scene),
                    MarkerKind.SceneAnchor
                )
            );
        if (editing)
            TakeClicks(view, markers, edited);
    }

    /// <summary>Makes the markers under the mouse clickable, and runs the gizmos and marker clicks.</summary>
    private void TakeClicks(EditorView view, List<TrackMarker> markers, Guid edited)
    {
        var io = ImGui.GetIO();
        var hovered = TrackMarkerHitTest.Nearest(markers, edited, io.MousePos, HitRadius);

        // The window takes the mouse only over a marker, so those clicks never reach the game.
        var flags = BaseFlags;
        if (hovered is null && !clicks.HoldingMarker && !(gizmo.Hot || anchorGizmo.Hot))
            flags |= ImGuiWindowFlags.NoInputs;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(view.Origin);
        ImGui.SetNextWindowSize(view.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("##vista-editor", flags))
        {
            ImGuizmo.BeginFrame();
            gizmo.Draw(view, session);
            anchorGizmo.Draw(view, session);

            // Dalamud hides presses from ImGui unless it wants the mouse, so read the button itself.
            var overUi = io.WantCaptureMouse && !ImGui.IsWindowHovered();
            var look = game.CameraAngles ?? (0f, 0f);
            Apply(
                clicks.Update(
                    PhysicalKeys.IsDown(VirtualKey.LBUTTON),
                    io.MousePos,
                    look,
                    overUi,
                    gizmo.Hot || anchorGizmo.Hot,
                    hovered
                ),
                markers
            );
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>Draws a track's anchor and Look At point where placed, adding their markers.</summary>
    private static void DrawAnchors(
        EditorView view,
        Scene scene,
        Track local,
        Track world,
        bool edited,
        AnchorKind? selected,
        List<TrackMarker> markers
    )
    {
        if (local.ShowsAnchor)
            markers.Add(
                new TrackMarker(
                    local.Id,
                    -1,
                    Overlay.DrawTrackAnchor(
                        view,
                        SceneGeometry.WorldAnchor(scene, local),
                        FirstPosition(world),
                        edited,
                        selected == AnchorKind.Track,
                        local.Name
                    ),
                    MarkerKind.TrackAnchor
                )
            );
        if (local.UsesLookAt)
            markers.Add(
                new TrackMarker(
                    local.Id,
                    -1,
                    Overlay.DrawLookAt(view, world.LookAt, FirstPosition(world), edited, selected == AnchorKind.LookAt),
                    MarkerKind.LookAt
                )
            );
    }

    /// <summary>Selects a clicked point, anchor or Look At point, switching to its track first when it isn't the edited one; a click on empty space clears the selection. With Ctrl or Shift, only the edited track's points respond.</summary>
    private void Apply(ClickOutcome outcome, List<TrackMarker> markers)
    {
        var click = RowPicking.FromKeys(PhysicalKeys.IsDown(VirtualKey.SHIFT), PhysicalKeys.IsDown(VirtualKey.CONTROL));
        if (click != RowClick.Plain)
        {
            if (
                outcome.Kind == ClickKind.Select
                && outcome.Index < markers.Count
                && markers[outcome.Index] is { Kind: MarkerKind.Point } point
                && point.Track == session.EditedTrackId
            )
                session.Selection.ClickPoint(point.Point, click);
            return;
        }

        switch (outcome.Kind)
        {
            case ClickKind.Select when outcome.Index < markers.Count:
                var hit = markers[outcome.Index];
                var refusal = hit.Kind switch
                {
                    MarkerKind.SceneAnchor => session.Selection.SelectSceneAnchor(),
                    MarkerKind.TrackAnchor => session.Selection.SelectTrackAnchor(hit.Track),
                    MarkerKind.LookAt => session.Selection.SelectLookAt(hit.Track),
                    _ when hit.Track == session.EditedTrackId => Select(hit.Point),
                    _ => session.SelectPoint(hit.Track, hit.Point),
                };
                Report(refusal);
                break;
            case ClickKind.Deselect:
                session.Selection.Select(null);
                break;
        }
    }

    private string? Select(int point)
    {
        session.Selection.Select(point);
        return null;
    }

    private static Vector3? FirstPosition(Track world) => world.Points.Count > 0 ? world.Points[0].Position : null;

    private static void AddMarkers(List<TrackMarker> markers, Guid track, IReadOnlyList<Vector2?> screens)
    {
        for (var i = 0; i < screens.Count; i++)
            markers.Add(new TrackMarker(track, i, screens[i]));
    }

    private static void Report(string? refusal)
    {
        if (refusal is not null)
            Plugin.Log.Warning("[editor] {Refusal}", refusal);
    }
}
