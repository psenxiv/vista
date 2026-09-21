using System.Numerics;
using CinematicCam.Core.Camera;
using CinematicCam.Plugin.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface.Utility;

namespace CinematicCam.Plugin.Probes;

/// <summary>Probe 1: which view matrix projects correctly, and whether the gizmo drags along the axis it draws.</summary>
internal sealed unsafe class GizmoProbe
{
    private const uint Red = 0xFF0000FF;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFFFF0000;

    private bool enabled;
    private bool useOurView;
    private Matrix4x4 gizmo;
    private bool placed;
    private bool wasUsing;
    private Vector3 dragStart;
    private long lastLog;

    /// <summary>Toggles the probe; "ours" drives the gizmo with our own view matrix.</summary>
    public void Toggle(string argument)
    {
        useOurView = argument == "ours";
        enabled = !enabled || argument.Length > 0;
        placed = false;
        Plugin.Log.Information("[probe] gizmo {State}, view {View}", enabled ? "on" : "off", useOurView ? "ours" : "game");
    }

    /// <summary>Draws the markers and gizmo. Call from UiBuilder.Draw.</summary>
    public void Draw(CameraState? frame)
    {
        if (!enabled || Plugin.Objects.LocalPlayer is not { } player) return;
        if (!CameraAccess.TryGetWorldCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        var render = scene->RenderCamera;
        if (render == null) return;

        Matrix4x4 gameView = scene->ViewMatrix;
        Matrix4x4 projection = render->ProjectionMatrix;
        var ourView = frame is { } f
            ? Matrix4x4.CreateLookAt(f.Position, f.LookAt, CameraOrientation.UpFor(f.Position, f.LookAt, f.Roll))
            : gameView;

        var viewport = ImGuiHelpers.MainViewport;
        var feet = player.Position;
        var list = ImGui.GetBackgroundDrawList();
        Mark(list, ScreenProjection.Project(feet, gameView * projection, viewport.Size), viewport.Pos, Red, 16f);
        Mark(list, ScreenProjection.Project(feet, ourView * projection, viewport.Size), viewport.Pos, Green, 11f);
        if (Plugin.GameGui.WorldToScreen(feet, out var gui)) list.AddCircle(gui, 6f, Blue, 0, 2f);

        LogCameraPositions(gameView, frame);
        DrawGizmo(useOurView ? ourView : gameView, projection, render->NearPlane, render->FarPlane, feet);
    }

    private static void Mark(ImDrawListPtr list, Vector2? screen, Vector2 origin, uint colour, float radius)
    {
        if (screen is { } s) list.AddCircle(origin + s, radius, colour, 0, 2f);
    }

    private void LogCameraPositions(Matrix4x4 gameView, CameraState? frame)
    {
        var now = Environment.TickCount64;
        if (now - lastLog < 1000) return;
        lastLog = now;

        var fromGame = Matrix4x4.Invert(gameView, out var inverse) ? inverse.Translation : Vector3.Zero;
        Plugin.Log.Information("[probe] camera from game view {Game}, written {Ours}, gap {Gap:0.000}",
            fromGame, frame?.Position, frame is { } f ? Vector3.Distance(fromGame, f.Position) : -1f);
    }

    private void DrawGizmo(Matrix4x4 view, Matrix4x4 projection, float near, float far, Vector3 feet)
    {
        if (!placed)
        {
            gizmo = Matrix4x4.CreateTranslation(feet + new Vector3(0f, 2f, 0f));
            placed = true;
        }

        // BDTHPlugin's fix-up (reference only): re-express the game's reversed-Z, infinite-far projection for ImGuizmo.
        var clip = far / (far - near);
        projection.M43 = -(clip * near);
        projection.M33 = -((far + near) / (far - near));
        view.M44 = 1f;

        var viewport = ImGuiHelpers.MainViewport;
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoSavedSettings;

        if (ImGui.Begin("##ccam-probe-gizmo", flags))
        {
            ImGuizmo.BeginFrame();
            ImGuizmo.SetDrawlist();
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);

            var v = &view.M11;
            var p = &projection.M11;
            fixed (float* m = &gizmo.M11)
                ImGuizmo.Manipulate(v, p, ImGuizmoOperation.Translate, ImGuizmoMode.World, m, null, null, null, null);

            LogDrag();
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void LogDrag()
    {
        var usingNow = ImGuizmo.IsUsing();
        if (usingNow && !wasUsing) dragStart = gizmo.Translation;
        if (!usingNow && wasUsing)
        {
            var delta = gizmo.Translation - dragStart;
            Plugin.Log.Information("[probe] gizmo drag moved x {X:0.000} y {Y:0.000} z {Z:0.000}", delta.X, delta.Y, delta.Z);
        }

        wasUsing = usingNow;
    }
}
