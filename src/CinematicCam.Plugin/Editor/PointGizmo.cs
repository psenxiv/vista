using System.Numerics;
using CinematicCam.Core.Editing;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Session;
using Dalamud.Bindings.ImGuizmo;

namespace CinematicCam.Plugin.Editor;

/// <summary>The move and rotate gizmo on the selected point; a drag commits on release.</summary>
internal sealed unsafe class PointGizmo
{
    private Matrix4x4 matrix;
    private ControlPoint? dragStart;
    private int dragIndex;
    private Track? dragTrack;
    private bool waitForRelease;

    public GizmoMode Mode { get; set; } = GizmoMode.Move;

    /// <summary>True while a drag is in progress.</summary>
    public bool Dragging => dragStart is not null;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>The point being dragged as it would be if released now, or null.</summary>
    public (int Index, ControlPoint Point)? Preview { get; private set; }

    /// <summary>Switches between Move and Rotate, except mid-drag.</summary>
    public void Toggle()
    {
        if (!Dragging) Mode = Mode == GizmoMode.Move ? GizmoMode.Rotate : GizmoMode.Move;
    }

    /// <summary>Abandons any drag in progress without committing, for leaving editing mode.</summary>
    public void Cancel()
    {
        if (Dragging) waitForRelease = true;
        dragStart = null;
        Preview = null;
        dragTrack = null;
        Hot = false;
    }

    /// <summary>Draws the gizmo on the selected point into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, CameraSession session)
    {
        if (Dragging && (!ReferenceEquals(session.Track, dragTrack) || session.Selected != dragIndex))
        {
            dragStart = null;
            Preview = null;
            dragTrack = null;
            waitForRelease = true;
        }

        if (session.Selected is not { } index || index >= session.Track.Points.Count)
        {
            Hot = false;
            Preview = null;
            dragStart = null;
            return;
        }

        var point = session.Track.Points[index];
        var aim = session.Track.Aim;
        if (!Dragging) matrix = PoseMatrix.From(point.Position, point.Yaw, point.Pitch, point.Roll);

        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);

        var operation = Mode == GizmoMode.Move ? ImGuizmoOperation.Translate
            : aim == AimMode.AimKeys ? ImGuizmoOperation.RotateX | ImGuizmoOperation.RotateY | ImGuizmoOperation.RotateZ
            : ImGuizmoOperation.RotateZ;
        var space = Mode == GizmoMode.Move ? ImGuizmoMode.World : ImGuizmoMode.Local;

        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);

        var usingNow = ImGuizmo.IsUsing();
        Hot = usingNow || ImGuizmo.IsOver();

        if (waitForRelease)
        {
            if (!usingNow) waitForRelease = false;
            return;
        }

        if (usingNow)
        {
            if (dragStart is null) { dragStart = point; dragIndex = index; dragTrack = session.Track; }
            Preview = (dragIndex, GizmoEdit.Apply(dragStart, matrix, Mode, aim));
            return;
        }

        if (dragStart is { } start)
        {
            var edited = GizmoEdit.Apply(start, matrix, Mode, aim);
            dragStart = null;
            Preview = null;
            dragTrack = null;
            if (!ReferenceEquals(edited, start) && session.ReplacePoint(dragIndex, edited) is { } refusal)
                Plugin.Log.Warning("[editor] gizmo edit refused: {Refusal}", refusal);
        }
    }
}
