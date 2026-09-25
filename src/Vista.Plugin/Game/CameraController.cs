using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using Vista.Core.Camera;

namespace Vista.Plugin.Game;

/// <summary>Owns the game camera by hooking CameraBase.Update.</summary>
internal sealed unsafe class CameraController : IDisposable
{
    private delegate void CameraUpdateDelegate(CameraBase* camera);

    /// <summary>The camera update hook's name, as a touch point and as the place its faults are recorded.</summary>
    public const string Name = "camera update hook";

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

    /// <summary>Whether the update hook installed: null until the world camera exists and it's been tried.</summary>
    public bool? Hooked { get; private set; }

    /// <summary>Installs the hook once a camera exists, trying only once. Safe to call repeatedly.</summary>
    public void TryInstallHook()
    {
        if (Hooked is not null)
            return;

        try
        {
            if (!CameraAccess.TryGetWorldCamera(out var camera))
                return;
            Install(camera);
            Hooked = true;
        }
        catch (Exception ex)
        {
            Hooked = false;
            Plugin.Log.Error(ex, "[camera] update hook did not install");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Install(Camera* camera)
    {
        var updateAddress = (nint)camera->CameraBase.VirtualTable->Update;
        Plugin.Log.Debug("[camera] hooking Update at 0x{Addr:X}", updateAddress);

        updateHook = Plugin.Hooks.HookFromAddress<CameraUpdateDelegate>(updateAddress, UpdateDetour);
        updateHook.Enable();
    }

    private void UpdateDetour(CameraBase* camera)
    {
#if DEBUG
        SelfTestReadBack();
#endif
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
            faults.Record(Name, ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Write()
    {
#if DEBUG
        if (selfTestThrow)
        {
            selfTestThrow = false;
            throw new InvalidOperationException("A deliberate fault from the self-test.");
        }

        if (selfTestFrames is { } frames)
        {
            if (frames() is { } written)
                CameraAccess.WriteState(written);
            return;
        }
#endif
        if (stateSource() is { } desired)
            CameraAccess.WriteState(desired);
    }

#if DEBUG
    private Func<CameraState?>? selfTestFrames;
    private Action? selfTestReadBack;
    private bool selfTestThrow;

    /// <summary>While a self-test runs: <paramref name="frames"/> replaces Vista's own frames, and <paramref name="readBack"/> runs at the start of each hook call, before the game's update. Null clears both.</summary>
    public void SelfTestProbe(Func<CameraState?>? frames, Action? readBack)
    {
        selfTestFrames = frames;
        selfTestReadBack = readBack;
    }

    /// <summary>Makes the hook's next write throw inside its own safety net, as a real fault would.</summary>
    public void SelfTestThrowOnce() => selfTestThrow = true;

    /// <summary>Runs the self-test's read-back, recording a fault rather than letting it reach the game.</summary>
    private void SelfTestReadBack()
    {
        if (selfTestReadBack is not { } readBack || faults.Any)
            return;
        try
        {
            readBack();
        }
        catch (Exception ex)
        {
            faults.Record("self-test read-back", ex);
        }
    }
#endif

    public void Dispose()
    {
        updateHook?.Disable();
        updateHook?.Dispose();
        Plugin.Log.Debug("[camera] hook disposed after {Count} updates.", UpdateCount);
    }
}
