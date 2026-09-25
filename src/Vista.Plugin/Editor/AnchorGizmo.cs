using System.Numerics;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Game.ClientState.Keys;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Editor;

/// <summary>The move gizmo, in world or the anchor's own space, and yaw ring on the selected anchor, or the move gizmo alone on the Look At point; a drag previews live and holding Alt moves an anchor alone.</summary>
internal sealed class AnchorGizmo
{
    private const int MoveId = 10;
    private const int YawId = 11;

    private readonly PointGizmo points;
    private Matrix4x4 matrix;
    private Anchor? dragStart;
    private AnchorKind? dragKind;
    private Guid dragTrack;
    private bool dragRotate;
    private bool dragLocal;
    private bool waitForRelease;

    public AnchorGizmo(PointGizmo points) => this.points = points;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>Ends a drag in progress, keeping what it moved, for leaving editing mode.</summary>
    public void Cancel(SessionState session)
    {
        if (dragStart is not null)
            session.EndLiveEdit();
        dragStart = null;
        Hot = false;
    }

    /// <summary>Draws the gizmo on the selected anchor into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, SessionState session)
    {
        // An undo mid-drag ends the live edit; the rest of that drag does nothing, as with the fields.
        if (
            dragStart is not null
            && (session.Selection.Anchor != dragKind || session.EditedTrackId != dragTrack || !session.LiveEditing)
        )
        {
            session.EndLiveEdit();
            dragStart = null;
            waitForRelease = true;
            Gizmo.Reset();
        }

        if (session.Selection.Anchor is not { } kind || Shown(session, kind) is not { } anchor)
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
        if (dragStart is null)
            matrix = PoseMatrix.From(shown.Position, shown.Yaw, 0f, 0f);
        var rotate =
            kind != AnchorKind.LookAt && (dragStart is not null ? dragRotate : points.Mode == GizmoMode.Rotate);
        // The Look At point has no heading, so its local axes are the world's anyway.
        var local = dragStart is not null ? dragLocal : points.Mode == GizmoMode.MoveLocal;
        ImGuizmo.SetID(rotate ? YawId : MoveId);
        Gizmo.Manipulate(
            view,
            rotate ? ImGuizmoOperation.RotateY : ImGuizmoOperation.Translate,
            rotate || local ? ImGuizmoMode.Local : ImGuizmoMode.World,
            ref matrix
        );
        var usingNow = ImGuizmo.IsUsing();
        Hot = usingNow || ImGuizmo.IsOver();

        if (waitForRelease)
        {
            if (!usingNow)
                waitForRelease = false;
            return;
        }

        if (usingNow)
        {
            if (dragStart is null)
            {
                dragStart = anchor;
                dragKind = kind;
                dragTrack = session.EditedTrackId;
                dragRotate = rotate;
                dragLocal = local;
                session.BeginLiveEdit();
            }

            var edited = GizmoEdit.MoveAnchor(dragStart.Value, matrix, rotate);
            var refusal =
                kind == AnchorKind.LookAt
                    ? session.PreviewLookAt(edited.Position)
                    : session.PreviewAnchor(edited, carry: !PhysicalKeys.IsDown(VirtualKey.MENU));
            if (refusal is not null)
            {
                Report(refusal);
                dragStart = null;
                waitForRelease = true;
                Gizmo.Reset();
            }

            return;
        }

        if (dragStart is not null)
        {
            dragStart = null;
            session.EndLiveEdit();
        }
    }

    /// <summary>The selected anchor in the world, or the Look At point as an anchor with no yaw.</summary>
    private static Anchor? Shown(SessionState session, AnchorKind kind)
    {
        if (kind != AnchorKind.LookAt)
            return session.Selection.AnchorInWorld;
        return session.Selection.LookAtInWorld is { } point ? new Anchor(point, 0f) : null;
    }
}
