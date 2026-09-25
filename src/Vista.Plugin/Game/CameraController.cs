using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using Vista.Core.Camera;

namespace Vista.Plugin.Game;

/// <summary>Owns the game camera by hooking CameraBase.Update.</summary>
internal sealed unsafe class CameraController : IDisposable
{
    private delegate void CameraUpdateDelegate(CameraBase* camera);

    private readonly Func<CameraState?> stateSource;
    private readonly Faults faults;
    private Hook<CameraUpdateDelegate>? updateHook;

    public long UpdateCount { get; private set; }

    public CameraController(Func<CameraState?> stateSource, Faults faults)
    {
        this.stateSource = stateSource;
        this.faults = faults;
        TryInstallHook();
    }

    /// <summary>Installs the hook if a camera exists yet. Safe to call repeatedly.</summary>
    public void TryInstallHook()
    {
        if (updateHook != null)
            return;
        if (!CameraAccess.TryGetWorldCamera(out var camera))
            return;

        var updateAddress = (nint)camera->CameraBase.VirtualTable->Update;
        Plugin.Log.Debug("[camera] hooking Update at 0x{Addr:X}", updateAddress);

        updateHook = Plugin.Hooks.HookFromAddress<CameraUpdateDelegate>(updateAddress, UpdateDetour);
        updateHook.Enable();
    }

    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;
        if (faults.Any)
            return;

        try
        {
            Write();
        }
        catch (Exception ex)
        {
            faults.Record("camera update hook", ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Write()
    {
        if (stateSource() is { } desired)
            CameraAccess.WriteState(desired);
    }

    public void Dispose()
    {
        updateHook?.Disable();
        updateHook?.Dispose();
        Plugin.Log.Debug("[camera] hook disposed after {Count} updates.", UpdateCount);
    }
}
