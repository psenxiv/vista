using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Plugin.Game;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Editor;

/// <summary>This frame's world-camera matrices and the main viewport, for the overlay and the gizmo.</summary>
internal readonly record struct EditorView(
    Matrix4x4 ViewProjection, Matrix4x4 GizmoView, Matrix4x4 GizmoProjection,
    float Near, Vector2 Origin, Vector2 Size, Vector3 CameraRight)
{
    /// <summary>Absolute screen position of <paramref name="world"/>, or null when it is behind the camera.</summary>
    public Vector2? ToScreen(Vector3 world)
        => ScreenProjection.Project(world, ViewProjection, Size) is { } p ? Origin + p : null;

    /// <summary>Like <see cref="ToScreen"/>, but null nearer than the near plane, where a point would project wildly off screen.</summary>
    public Vector2? ToScreenBeyondNear(Vector3 world)
        => ScreenProjection.Project(world, ViewProjection, Size, Near) is { } p ? Origin + p : null;

    /// <summary>Reads the world camera's matrices, or null when there is no camera yet.</summary>
    public static unsafe EditorView? Read()
    {
        if (!CameraAccess.TryGetWorldCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        var render = scene->RenderCamera;
        if (render == null) return null;

        Matrix4x4 view = scene->ViewMatrix;
        Matrix4x4 projection = render->ProjectionMatrix;
        var near = render->NearPlane;
        var far = render->FarPlane;

        // BDTHPlugin's fix-up: re-express the game's reversed-Z, infinite-far projection for ImGuizmo.
        var gizmoProjection = projection;
        gizmoProjection.M43 = -(far / (far - near) * near);
        gizmoProjection.M33 = -((far + near) / (far - near));
        var gizmoView = view;
        gizmoView.M44 = 1f;

        var right = Matrix4x4.Invert(view, out var inverse)
            ? Vector3.Normalize(new Vector3(inverse.M11, inverse.M12, inverse.M13))
            : Vector3.UnitX;

        var viewport = ImGuiHelpers.MainViewport;
        return new EditorView(view * projection, gizmoView, gizmoProjection, near, viewport.Pos, viewport.Size, right);
    }
}
