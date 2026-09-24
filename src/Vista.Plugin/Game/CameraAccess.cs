using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Vista.Core.Camera;

namespace Vista.Plugin.Game;

/// <summary>Reads and writes the world camera.</summary>
internal static unsafe class CameraAccess
{
    /// <summary>The world camera. Not the active one, which is the lobby camera at the title screen.</summary>
    public static bool TryGetWorldCamera(out Camera* camera)
    {
        camera = null;
        var manager = CameraManager.Instance();
        if (manager == null)
            return false;

        camera = manager->Camera;
        return camera != null;
    }

    /// <summary>Reads the camera's current position, look-at and field of view.</summary>
    public static CameraState? ReadState()
    {
        if (!TryGetWorldCamera(out var camera))
            return null;

        var scene = &camera->CameraBase.SceneCamera;
        var position = scene->Object.Position;
        var lookAt = scene->LookAtVector;
        var up = CameraRotation.Up(CameraRotation.FromBasis(lookAt - position, scene->Vector_1));
        return new CameraState(position, lookAt, up, camera->FoV);
    }

    /// <summary>The world camera's yaw and pitch in radians, as DirH and DirV.</summary>
    public static (float Yaw, float Pitch)? ReadAngles()
    {
        if (!TryGetWorldCamera(out var camera))
            return null;
        return (camera->DirH, camera->DirV);
    }

    /// <summary>Sets the world camera's yaw and pitch in radians, as DirH and DirV; probe 2 showed both are runtime state, not saved settings.</summary>
    public static void WriteAngles(float yaw, float pitch)
    {
        if (!TryGetWorldCamera(out var camera))
            return;
        camera->DirH = yaw;
        camera->DirV = pitch;
    }

    /// <summary>Everything WriteState touches, so release can put it all back.</summary>
    public readonly record struct Snapshot(Vector3 Position, Vector3 LookAt, Vector3 Up, float Fov);

    /// <summary>Captures the game's camera state before we start overwriting it.</summary>
    public static Snapshot? Capture()
    {
        if (!TryGetWorldCamera(out var camera))
            return null;

        var scene = &camera->CameraBase.SceneCamera;
        return new Snapshot(scene->Object.Position, scene->LookAtVector, scene->Vector_1, camera->FoV);
    }

    /// <summary>Puts back everything WriteState changed.</summary>
    public static void Restore(Snapshot snapshot)
    {
        if (!TryGetWorldCamera(out var camera))
            return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = snapshot.Position;
        scene->LookAtVector = snapshot.LookAt;
        scene->Vector_1 = snapshot.Up;
        camera->FoV = snapshot.Fov;
    }

    /// <summary>Overwrites camera position, look-at and up; the up is written as given, so any orientation, inverted included, reaches the game.</summary>
    public static void WriteState(CameraState state)
    {
        if (!TryGetWorldCamera(out var camera))
            return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = state.Position;
        scene->LookAtVector = state.LookAt;
        scene->Vector_1 = state.Up;
        camera->FoV = state.Fov;

        // Distance and InterpDistance are deliberately NOT written. They are saved
        // settings: writing them corrupted a character's stored camera on 2026-09-21,
        // surviving relog, client restart and disabling Dalamud. See the spec.
        // Scrolling during a takeover can jitter slightly; suppress the input instead.
    }
}
