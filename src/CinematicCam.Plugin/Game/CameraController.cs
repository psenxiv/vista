using CinematicCam.Core;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CinematicCam.Plugin.Game;

/// <summary>Owns the game camera by hooking CameraBase.Update (vfunc 3).</summary>
internal sealed unsafe class CameraController : IDisposable
{
    private const int UpdateVFuncIndex = 3;

    private delegate void CameraUpdateDelegate(CameraBase* camera);

    private readonly Func<CameraState?> stateSource;
    private readonly Hook<CameraUpdateDelegate>? updateHook;

    public bool IsHooked => updateHook?.IsEnabled == true;
    public long UpdateCount { get; private set; }

    public CameraController(Func<CameraState?> stateSource)
    {
        this.stateSource = stateSource;

        if (!CameraAccess.TryGetActiveCamera(out var camera))
        {
            Plugin.Log.Error("[camera] no active camera at construction; hook not installed.");
            return;
        }

        var vtable = *(nint**)camera;
        var updateAddress = vtable[UpdateVFuncIndex];
        Plugin.Log.Information("[camera] hooking Update at 0x{Addr:X}", updateAddress);

        updateHook = Plugin.Hooks.HookFromAddress<CameraUpdateDelegate>(updateAddress, UpdateDetour);
        updateHook.Enable();
    }

    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;
    }

    public void Dispose()
    {
        updateHook?.Disable();
        updateHook?.Dispose();
        Plugin.Log.Information("[camera] hook disposed after {Count} updates.", UpdateCount);
    }
}
