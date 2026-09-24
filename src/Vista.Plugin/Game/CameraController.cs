using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using Vista.Core.Camera;

namespace Vista.Plugin.Game;

/// <summary>Owns the game camera by hooking CameraBase.Update (vfunc 3).</summary>
internal sealed unsafe class CameraController : IDisposable
{
    private const int UpdateVFuncIndex = 3;

    private delegate void CameraUpdateDelegate(CameraBase* camera);

    private readonly Func<CameraState?> stateSource;
    private Hook<CameraUpdateDelegate>? updateHook;

    public long UpdateCount { get; private set; }

    /// <summary>True after the update hook caught an exception; it writes nothing until <see cref="ClearFault"/>.</summary>
    public bool Faulted { get; private set; }

    /// <summary>Lets the update hook write again.</summary>
    public void ClearFault() => Faulted = false;

    public CameraController(Func<CameraState?> stateSource)
    {
        this.stateSource = stateSource;
        TryInstallHook();
    }

    /// <summary>Installs the hook if a camera exists yet. Safe to call repeatedly.</summary>
    public void TryInstallHook()
    {
        if (updateHook != null)
            return;
        if (!CameraAccess.TryGetWorldCamera(out var camera))
            return;

        var vtable = *(nint**)camera;
        var updateAddress = vtable[UpdateVFuncIndex];
        Plugin.Log.Debug("[camera] hooking Update at 0x{Addr:X}", updateAddress);

        updateHook = Plugin.Hooks.HookFromAddress<CameraUpdateDelegate>(updateAddress, UpdateDetour);
        updateHook.Enable();
    }

    private void UpdateDetour(CameraBase* camera)
    {
        updateHook!.Original(camera);
        UpdateCount++;
        if (Faulted)
            return;

        try
        {
            var desired = stateSource();
            if (desired is null)
                return;

            CameraAccess.WriteState(desired.Value);
        }
        catch (Exception ex)
        {
            Faulted = true;
            Plugin.Log.Error(ex, "[camera] update hook failed; releasing on the next framework update.");
        }
    }

    public void Dispose()
    {
        updateHook?.Disable();
        updateHook?.Dispose();
        Plugin.Log.Debug("[camera] hook disposed after {Count} updates.", UpdateCount);
    }
}
