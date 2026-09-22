using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;

namespace Vista.Plugin.Editor;

/// <summary>The move gizmo and yaw ring on the selected anchor; a drag previews live and holding Alt moves the anchor alone.</summary>
internal sealed unsafe class AnchorGizmo
{
    private const int MoveId = 10;
    private const int YawId = 11;

    private readonly PointGizmo points;
    private Matrix4x4 matrix;
    private Anchor? dragStart;
    private AnchorKind? dragKind;
    private Guid dragTrack;
    private bool waitForRelease;

    public AnchorGizmo(PointGizmo points) => this.points = points;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>Abandons a drag in progress, ending its live edit, for leaving editing mode.</summary>
    public void Cancel(CameraSession session)
    {
        if (dragStart is not null) session.EndLiveEdit();
        dragStart = null;
        Hot = false;
    }

    /// <summary>Draws the gizmo on the selected anchor into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, CameraSession session)
    {
        if (dragStart is not null && (session.SelectedAnchor != dragKind || session.EditedTrackId != dragTrack))
        {
            session.EndLiveEdit();
            dragStart = null;
            waitForRelease = true;
            Reset();
        }

        if (session.SelectedAnchor is not { } kind || session.SelectedAnchorInWorld is not { } anchor)
        {
            Hot = false;
            dragStart = null;
            return;
        }

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);

        var shown = dragStart ?? anchor;
        if (dragStart is null) matrix = PoseMatrix.From(shown.Position, shown.Yaw, 0f, 0f);
        var rotate = points.Mode == GizmoMode.Rotate;
        ImGuizmo.SetID(rotate ? YawId : MoveId);
        Manipulate(view, rotate ? ImGuizmoOperation.RotateY : ImGuizmoOperation.Translate, rotate ? ImGuizmoMode.Local : ImGuizmoMode.World);
        var usingNow = ImGuizmo.IsUsing();
        Hot = usingNow || ImGuizmo.IsOver();

        if (waitForRelease)
        {
            if (!usingNow) waitForRelease = false;
            return;
        }

        if (usingNow)
        {
            if (dragStart is null)
            {
                dragStart = anchor;
                dragKind = kind;
                dragTrack = session.EditedTrackId;
                session.BeginLiveEdit();
            }

            var edited = rotate
                ? dragStart.Value with { Yaw = TrackAim.FromDirection(-new Vector3(matrix.M31, matrix.M32, matrix.M33)).Yaw }
                : dragStart.Value with { Position = matrix.Translation };
            var carry = !PhysicalKeys.IsDown(VirtualKey.MENU);
            if (session.PreviewAnchor(edited, carry) is { } refusal) Plugin.Log.Warning("[editor] anchor drag refused: {Refusal}", refusal);
            return;
        }

        if (dragStart is not null)
        {
            dragStart = null;
            session.EndLiveEdit();
        }
    }

    private void Manipulate(EditorView view, ImGuizmoOperation operation, ImGuizmoMode space)
    {
        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);
    }

    private static void Reset()
    {
        ImGuizmo.Enable(false);
        ImGuizmo.Enable(true);
    }
}
