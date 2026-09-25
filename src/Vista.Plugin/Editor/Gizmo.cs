using System.Numerics;
using Dalamud.Bindings.ImGuizmo;

namespace Vista.Plugin.Editor;

/// <summary>The ImGuizmo calls the point and anchor gizmos share, and waiting out a drag one of them dropped.</summary>
internal static unsafe class Gizmo
{
    /// <summary>Sets ImGuizmo up to draw over <paramref name="view"/> in the current window.</summary>
    public static void Begin(EditorView view)
    {
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(view.Origin.X, view.Origin.Y, view.Size.X, view.Size.Y);
        ImGuizmo.AllowAxisFlip(false);
    }

    /// <summary>Runs one manipulation against <paramref name="matrix"/>, which it edits in place.</summary>
    public static void Manipulate(
        EditorView view,
        ImGuizmoOperation operation,
        ImGuizmoMode space,
        ref Matrix4x4 matrix
    )
    {
        var gizmoView = view.GizmoView;
        var gizmoProjection = view.GizmoProjection;
        fixed (float* m = &matrix.M11)
            ImGuizmo.Manipulate(&gizmoView.M11, &gizmoProjection.M11, operation, space, m, null, null, null, null);
    }

    /// <summary>Drops the drag in progress: <paramref name="waiting"/> holds until the mouse lets go, and ImGuizmo's own drag is cleared.</summary>
    public static void Drop(ref bool waiting)
    {
        waiting = true;
        Reset();
    }

    /// <summary>True while a dropped drag is still held, with <paramref name="usingNow"/> as ImGuizmo reports it; clears <paramref name="waiting"/> once it's let go.</summary>
    public static bool StillHeld(ref bool waiting, bool usingNow)
    {
        if (!waiting)
            return false;
        if (!usingNow)
            waiting = false;
        return true;
    }

    /// <summary>Clears ImGuizmo's own drag, which otherwise ends only inside the owning Manipulate.</summary>
    private static void Reset()
    {
        ImGuizmo.Enable(false);
        ImGuizmo.Enable(true);
    }
}
