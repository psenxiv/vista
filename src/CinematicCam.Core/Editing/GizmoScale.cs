using System.Numerics;

namespace CinematicCam.Core.Editing;

/// <summary>Keeps the gizmo a fixed size on screen when it is drawn with different matrices from the screen's.</summary>
public static class GizmoScale
{
    private const float Epsilon = 1e-6f;

    /// <summary>The clip-space size to give ImGuizmo so the gizmo appears <paramref name="target"/> tall on screen; <paramref name="target"/> itself if either length is degenerate.</summary>
    public static float ClipSize(Vector3 position, Vector3 cameraRight, Matrix4x4 gizmoViewProjection, Matrix4x4 screenViewProjection, float target)
    {
        var drawn = ClipLength(position, cameraRight, gizmoViewProjection);
        var actual = ClipLength(position, cameraRight, screenViewProjection);
        return drawn > Epsilon && actual > Epsilon ? target * drawn / actual : target;
    }

    private static float ClipLength(Vector3 position, Vector3 direction, Matrix4x4 viewProjection)
    {
        var a = Vector4.Transform(new Vector4(position, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(position + direction, 1f), viewProjection);
        if (a.W <= Epsilon || b.W <= Epsilon) return 0f;
        return Vector2.Distance(new Vector2(a.X, a.Y) / a.W, new Vector2(b.X, b.Y) / b.W);
    }
}
