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
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Game;

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

    private readonly SessionState session;
    private readonly PointGizmo gizmo;
    private readonly AnchorGizmo anchorGizmo;
    private readonly Overlay overlay = new();
    private readonly ClickSelection clicks = new();

    /// <summary>True while the edited track's path is drawn by how fast its camera turns.</summary>
    public bool Heat { get; set; }

    public EditorLayer(SessionState session, PointGizmo gizmo)
    {
        this.session = session;
        this.gizmo = gizmo;
        anchorGizmo = new AnchorGizmo(gizmo);
    }

    /// <summary>Draws the editor for this frame. Call from UiBuilder.Draw.</summary>
    public void Draw()
    {
        var editing = session.Mode == CameraMode.Editing && !session.Transport.Previewing;
        if (!editing)
        {
            clicks.Reset();
            gizmo.Cancel();
            anchorGizmo.Cancel(session);
        }
        if (!editing && session.Mode != CameraMode.View)
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
                overlay.DrawTargetMarker(view, otherTarget, FirstPosition(otherWorld), edited: false);
            if (other is { AnchorPlaced: true, Aim: not AimMode.FollowTarget })
                markers.Add(
                    new TrackMarker(
                        other.Id,
                        -1,
                        overlay.DrawTrackAnchor(
                            view,
                            SceneGeometry.WorldAnchor(scene, other),
                            FirstPosition(otherWorld),
                            edited: false,
                            selected: false,
                            other.Name
                        ),
                        MarkerKind.TrackAnchor
                    )
                );
            if (other is { Aim: AimMode.LookAt, LookAtPlaced: true })
                markers.Add(
                    new TrackMarker(
                        other.Id,
                        -1,
                        overlay.DrawLookAt(
                            view,
                            otherWorld.LookAt,
                            FirstPosition(otherWorld),
                            edited: false,
                            selected: false
                        ),
                        MarkerKind.LookAt
                    )
                );
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
        if (editedLocal is { AnchorPlaced: true, Aim: not AimMode.FollowTarget })
            markers.Add(
                new TrackMarker(
                    edited,
                    -1,
                    overlay.DrawTrackAnchor(
                        view,
                        SceneGeometry.WorldAnchor(scene, editedLocal),
                        FirstPosition(track),
                        edited: true,
                        selected: selectedAnchor == AnchorKind.Track,
                        editedLocal.Name
                    ),
                    MarkerKind.TrackAnchor
                )
            );
        if (editedLocal is { Aim: AimMode.LookAt, LookAtPlaced: true })
            markers.Add(
                new TrackMarker(
                    edited,
                    -1,
                    overlay.DrawLookAt(
                        view,
                        track.LookAt,
                        FirstPosition(track),
                        edited: true,
                        selected: selectedAnchor == AnchorKind.LookAt
                    ),
                    MarkerKind.LookAt
                )
            );
        if (session.World.TargetPoint(track) is { } target)
            overlay.DrawTargetMarker(view, target, FirstPosition(track), edited: true);
        if (scene.AnchorPlaced)
            markers.Add(
                new TrackMarker(
                    Guid.Empty,
                    -1,
                    overlay.DrawSceneAnchor(view, scene.Anchor, selectedAnchor == AnchorKind.Scene),
                    MarkerKind.SceneAnchor
                )
            );
        if (!editing)
            return;

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
            var look = CameraAccess.ReadAngles() ?? (0f, 0f);
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

    /// <summary>Selects a clicked point, anchor or Look At point, switching to its track first when it isn't the edited one; a click on empty space clears the selection. With Ctrl or Shift, only the edited track's points respond.</summary>
    private void Apply(ClickOutcome outcome, IReadOnlyList<TrackMarker> markers)
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
