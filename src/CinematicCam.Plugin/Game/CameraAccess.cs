using System.Numerics;
using CinematicCam.Core.Camera;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace CinematicCam.Plugin.Game;

/// <summary>Reads and writes the world camera.</summary>
internal static unsafe class CameraAccess
{
    /// <summary>The world camera. Not the active one, which is the lobby camera at the title screen.</summary>
    public static bool TryGetWorldCamera(out Camera* camera)
    {
        camera = null;
        var manager = CameraManager.Instance();
        if (manager == null) return false;

        camera = manager->Camera;
        return camera != null;
    }

    /// <summary>Reads the camera's current position, look-at and field of view.</summary>
    public static CameraState? ReadState()
    {
        if (!TryGetWorldCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new CameraState(scene->Object.Position, scene->LookAtVector, camera->FoV);
    }

    /// <summary>The world camera's yaw and pitch in radians, as DirH and DirV.</summary>
    public static (float Yaw, float Pitch)? ReadAngles()
    {
        if (!TryGetWorldCamera(out var camera)) return null;
        return (camera->DirH, camera->DirV);
    }

    /// <summary>Everything WriteState touches, so release can put it all back.</summary>
    public readonly record struct Snapshot(Vector3 Position, Vector3 LookAt, Vector3 Up, float Fov);

    /// <summary>Captures the game's camera state before we start overwriting it.</summary>
    public static Snapshot? Capture()
    {
        if (!TryGetWorldCamera(out var camera)) return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new Snapshot(scene->Object.Position, scene->LookAtVector, scene->Vector_1, camera->FoV);
    }

    /// <summary>Puts back everything WriteState changed.</summary>
    public static void Restore(Snapshot snapshot)
    {
        if (!TryGetWorldCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = snapshot.Position;
        scene->LookAtVector = snapshot.LookAt;
        scene->Vector_1 = snapshot.Up;
        camera->FoV = snapshot.Fov;
    }

    /// <summary>Puts the field of view back to stock when no snapshot is available.</summary>
    public static void ResetToDefaults()
    {
        if (!TryGetWorldCamera(out var camera)) return;

        // Only FoV. Distance is a persisted setting, see WriteState.
        camera->FoV = 0.78f;

        Plugin.Log.Information("[ccam] field of view reset to stock.");
    }

    /// <summary>Roll in radians applied to every write; a probe for whether the renderer honours a rolled up vector.</summary>
    public static float ProbeRoll { get; set; }

    /// <summary>Overwrites camera position and look-at.</summary>
    public static void WriteState(CameraState state)
    {
        if (!TryGetWorldCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = state.Position;
        scene->LookAtVector = state.LookAt;
        scene->Vector_1 = CameraOrientation.UpFor(state.Position, state.LookAt, ProbeRoll);
        camera->FoV = state.Fov;

        // Distance and InterpDistance are deliberately NOT written. They are saved
        // settings: writing them corrupted a character's stored camera on 2026-09-21,
        // surviving relog, client restart and disabling Dalamud. See the spec.
        // Scrolling during a takeover can jitter slightly; suppress the input instead.
    }
}
