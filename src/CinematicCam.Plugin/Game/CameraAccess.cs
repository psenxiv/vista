using System.Numerics;
using CinematicCam.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace CinematicCam.Plugin.Game;

/// <summary>Reads and writes the active game camera.</summary>
internal static unsafe class CameraAccess
{
    public static bool TryGetActiveCamera(out Camera* camera)
    {
        camera = null;
        var manager = CameraManager.Instance();
        if (manager == null) return false;

        camera = manager->GetActiveCamera();
        return camera != null;
    }

    /// <summary>Reads the camera's current position, look-at and field of view.</summary>
    public static CameraState? ReadState()
    {
        if (!TryGetActiveCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new CameraState(scene->Object.Position, scene->LookAtVector, camera->FoV);
    }

    /// <summary>Everything WriteState touches, so release can put it all back.</summary>
    public readonly record struct Snapshot(
        Vector3 Position, Vector3 LookAt, Vector3 Up,
        float Fov, float Distance, float InterpDistance);

    /// <summary>Captures the game's camera state before we start overwriting it.</summary>
    public static Snapshot? Capture()
    {
        if (!TryGetActiveCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new Snapshot(
            scene->Object.Position, scene->LookAtVector, scene->Vector_1,
            camera->FoV, camera->Distance, camera->InterpDistance);
    }

    /// <summary>Puts back everything WriteState changed.</summary>
    public static void Restore(Snapshot snapshot)
    {
        if (!TryGetActiveCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = snapshot.Position;
        scene->LookAtVector = snapshot.LookAt;
        scene->Vector_1 = snapshot.Up;
        camera->FoV = snapshot.Fov;
        camera->Distance = snapshot.Distance;
        camera->InterpDistance = snapshot.InterpDistance;
    }

    /// <summary>Puts the camera back to stock values when no snapshot is available.</summary>
    public static void ResetToDefaults()
    {
        if (!TryGetActiveCamera(out var camera)) return;

        camera->FoV = 0.78f;
        camera->Distance = 6f;
        camera->InterpDistance = 6f;

        Plugin.Log.Information("[ccam] camera reset to stock values.");
    }

    /// <summary>Overwrites camera position and look-at.</summary>
    public static void WriteState(CameraState state)
    {
        if (!TryGetActiveCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = state.Position;
        scene->LookAtVector = state.LookAt;
        scene->Vector_1 = CameraOrientation.UpFor(state.Position, state.LookAt);
        camera->FoV = state.Fov;

        // Distance and InterpDistance are deliberately NOT written. They are saved
        // settings: writing them corrupted a character's stored camera on 2026-09-21,
        // surviving relog, client restart and disabling Dalamud. See the spec.
        // Scrolling during a takeover can jitter slightly; suppress the input instead.
    }
}
