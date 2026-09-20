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

    /// <summary>Overwrites camera position and look-at.</summary>
    public static void WriteState(CameraState state)
    {
        if (!TryGetActiveCamera(out var camera)) return;

        var scene = &camera->CameraBase.SceneCamera;
        scene->Object.Position = state.Position;
        scene->LookAtVector = state.LookAt;
        scene->Vector_1 = CameraOrientation.UpFor(state.Position, state.LookAt);
    }
}
