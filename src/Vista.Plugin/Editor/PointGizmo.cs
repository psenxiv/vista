using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGuizmo;

namespace Vista.Plugin.Editor;

/// <summary>The move gizmo and gimbal rotate rings on the selected point; a drag commits on release.</summary>
internal sealed unsafe class PointGizmo
{
    private const int MoveId = 1;
    private const int FirstRingId = 2;

    private static readonly GimbalRing[] AllRings = [GimbalRing.Yaw, GimbalRing.Pitch, GimbalRing.Roll];
    private static readonly GimbalRing[] RollOnly = [GimbalRing.Roll];

    private readonly Matrix4x4[] ringFrames = new Matrix4x4[AllRings.Length];
    private Matrix4x4 moveMatrix;
    private ControlPoint? dragStart;
    private GimbalRing? dragRing;
    private int dragIndex;
    private Track? dragTrack;
    private bool waitForRelease;

    public GizmoMode Mode { get; private set; } = GizmoMode.Move;

    /// <summary>True while a drag is in progress.</summary>
    public bool Dragging => dragStart is not null;

    /// <summary>True when the cursor was over the gizmo, or dragging it, at the last draw.</summary>
    public bool Hot { get; private set; }

    /// <summary>The point being dragged as it would be if released now, or null.</summary>
    public (int Index, ControlPoint Point)? Preview { get; private set; }

    /// <summary>Switches between Move and Rotate, except while the mouse is still held from a drag.</summary>
    public void Toggle() => SetMode(Mode == GizmoMode.Move ? GizmoMode.Rotate : GizmoMode.Move);

    /// <summary>Sets Move or Rotate, except while the mouse is still held from a drag.</summary>
    public void SetMode(GizmoMode mode)
    {
        if (!Dragging && !waitForRelease) Mode = mode;
    }

    /// <summary>Abandons any drag in progress without committing, for leaving editing mode.</summary>
    public void Cancel()
    {
        Abandon();
        Hot = false;
    }

    /// <summary>Draws the gizmo on the selected point into the current window. Call inside the editor window.</summary>
    public void Draw(EditorView view, CameraSession session)
    {
        if (Dragging && (!ReferenceEquals(session.StoredTrack, dragTrack) || session.Selected != dragIndex)) Abandon();

        if (session.Selected is not { } index || index >= session.Track.Points.Count)
        {
            Hot = false;
            Preview = null;
            dragStart = null;
            return;
        }

        var point = session.Track.Points[index];
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);

        var (usingNow, over, ring) = Mode == GizmoMode.Move
            ? DrawMove(view, point)
            : DrawRings(view, Preview?.Point ?? point, session.Track.Aim is AimMode.AimKeys or AimMode.WatchTarget or AimMode.FollowTarget ? AllRings : RollOnly);
        Hot = usingNow || over;

        if (waitForRelease)
        {
            if (!usingNow) waitForRelease = false;
            return;
        }

        if (usingNow)
        {
            if (dragStart is null)
            {
                // A ring drag already under way when we saw it has no known ring; wait it out.
                if (Mode == GizmoMode.Rotate && ring is null) { waitForRelease = true; ResetImGuizmo(); return; }
                dragStart = point;
                dragIndex = index;
                dragTrack = session.StoredTrack;
                dragRing = ring;
            }

            Preview = (dragIndex, Edited(dragStart));
            return;
        }

        if (dragStart is { } start)
        {
            var edited = Edited(start);
            dragStart = null;
            dragRing = null;
            Preview = null;
            dragTrack = null;
            if (!ReferenceEquals(edited, start) && session.ReplacePoint(dragIndex, edited) is { } refusal)
                Plugin.Log.Warning("[editor] gizmo edit refused: {Refusal}", refusal);
        }
    }

    private (bool Using, bool Over, GimbalRing? Ring) DrawMove(EditorView view, ControlPoint point)
    {
        if (!Dragging) moveMatrix = PoseMatrix.From(point.Position, point.Yaw, point.Pitch, point.Roll);

        ImGuizmo.SetID(MoveId);
        Manipulate(view, ImGuizmoOperation.Translate, ImGuizmoMode.World, ref moveMatrix);
        return (ImGuizmo.IsUsing(), ImGuizmo.IsOver(), null);
    }

    /// <summary>Draws each ring as its own gizmo in its own frame; rings not being dragged follow the preview.</summary>
    /// <remarks>Dalamud's ImGuizmo reports IsUsing for any gizmo, so the dragged ring is the one whose Manipulate started it.</remarks>
    private (bool Using, bool Over, GimbalRing? Ring) DrawRings(EditorView view, ControlPoint shown, GimbalRing[] rings)
    {
        var usingNow = ImGuizmo.IsUsing();
        var over = false;
        GimbalRing? started = null;

        foreach (var ring in rings)
        {
            var slot = (int)ring;
            if (!(Dragging && dragRing == ring)) ringFrames[slot] = GizmoEdit.RingFrame(shown, ring);

            ImGuizmo.SetID(FirstRingId + slot);
            Manipulate(view, Operation(ring), ImGuizmoMode.Local, ref ringFrames[slot]);
            over |= ImGuizmo.IsOver();
            if (usingNow || !ImGuizmo.IsUsing()) continue;
            usingNow = true;
            started = ring;
        }

        return (usingNow, over, started);
    }

    private static void Manipulate(EditorView view, ImGuizmoOperation operation, ImGuizmoMode space, ref Matrix4x4 matrix)
    {
        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);
    }

    private static ImGuizmoOperation Operation(GimbalRing ring) => ring switch
    {
        GimbalRing.Yaw => ImGuizmoOperation.RotateY,
        GimbalRing.Pitch => ImGuizmoOperation.RotateX,
        _ => ImGuizmoOperation.RotateZ,
    };

    /// <summary>The drag's start point with this frame's gizmo applied.</summary>
    private ControlPoint Edited(ControlPoint start)
        => dragRing is { } ring ? GizmoEdit.Rotate(start, ring, ringFrames[(int)ring]) : GizmoEdit.Move(start, moveMatrix);

    /// <summary>Drops the drag without committing, and clears ImGuizmo's own drag, which otherwise ends only inside the owning Manipulate.</summary>
    private void Abandon()
    {
        if (!Dragging) return;
        waitForRelease = true;
        dragStart = null;
        dragRing = null;
        dragTrack = null;
        Preview = null;
        ResetImGuizmo();
    }

    private static void ResetImGuizmo()
    {
        ImGuizmo.Enable(false);
        ImGuizmo.Enable(true);
    }
}
