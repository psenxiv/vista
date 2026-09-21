using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin;

/// <summary>Logs diagnostics that can only be observed in-game.</summary>
internal static unsafe class SelfTest
{
    public static void Run()
    {
        Plugin.Log.Information("[selftest] ---- begin ----");

        if (!CameraAccess.TryGetWorldCamera(out var camera))
        {
            Plugin.Log.Error("[selftest] active camera is null. Aborting.");
            return;
        }

        Plugin.Log.Information("[selftest] camera at 0x{Addr:X}", (nint)camera);

        var vtable = *(nint**)camera;
        Plugin.Log.Information("[selftest] vtable at 0x{Addr:X}", (nint)vtable);
        for (var i = 0; i < 6; i++)
            Plugin.Log.Information("[selftest] vfunc[{Index}] = 0x{Addr:X}", i, vtable[i]);

        var state = CameraAccess.ReadState();
        if (state is null)
        {
            Plugin.Log.Error("[selftest] ReadState returned null despite a valid camera.");
            return;
        }

        Plugin.Log.Information("[selftest] position={Pos} lookAt={Look} fov={Fov} dirH={H} dirV={V}",
            state.Value.Position, state.Value.LookAt, state.Value.Fov, camera->DirH, camera->DirV);

        var scene = &camera->CameraBase.SceneCamera;
        Plugin.Log.Information("[selftest] upVector={Up} tiltOffset={Tilt} distance={Dist} interpDist={IDist}",
            scene->Vector_1, camera->TiltOffset, camera->Distance, camera->InterpDistance);

        Plugin.Log.Information("[selftest] hooked={Hooked} updateCount={Count}",
            Plugin.Camera.IsHooked, Plugin.Camera.UpdateCount);

        Plugin.Log.Information("[selftest] ---- end ----");
    }
}
