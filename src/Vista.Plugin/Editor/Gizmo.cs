using System.Numerics;
using Dalamud.Bindings.ImGuizmo;

namespace Vista.Plugin.Editor;

/// <summary>The ImGuizmo calls the point and anchor gizmos share.</summary>
internal static unsafe class Gizmo
{
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

    /// <summary>Clears ImGuizmo's own drag, which otherwise ends only inside the owning Manipulate.</summary>
    public static void Reset()
    {
        ImGuizmo.Enable(false);
        ImGuizmo.Enable(true);
    }
}
