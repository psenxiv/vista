using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace Vista.Plugin.Game;

/// <summary>Stops flight keys firing their usual actions while the plugin flies the camera.</summary>
internal sealed unsafe class InputBlocker : IDisposable
{
    private delegate byte IsInputIdDelegate(InputData* self, InputId inputId);
    private delegate sbyte GetMouseWheelDelegate();

    // The wheel reader is the one query ClientStructs does not map.
    private const string MouseWheelSignature = "E8 ?? ?? ?? ?? F7 D8 48 8B CB";

    // The keys we fly with. The game does not route character movement through these
    // queries, so blocking them stops stray actions, not walking; MovementLock does that.
    private static readonly FrozenSet<InputId> Blocked = new[]
    {
        InputId.MOVE_FORE,
        InputId.MOVE_BACK,
        InputId.MOVE_LEFT,
        InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L,
        InputId.MOVE_STRIFE_R,
        InputId.JUMP,
        InputId.MOVE_DESCENT,
        InputId.MOVE_RETENTION,
    }.ToFrozenSet();

    private readonly Hook<IsInputIdDelegate>? longPressHook;
    private readonly Hook<IsInputIdDelegate>? pressedHook;
    private readonly Hook<IsInputIdDelegate>? downHook;
    private readonly Hook<IsInputIdDelegate>? releasedHook;
    private readonly Hook<GetMouseWheelDelegate>? mouseWheelHook;

    private readonly Func<bool> shouldBlock;
    private readonly Func<bool> shouldBlockEscape;
    private readonly Faults faults;

    public InputBlocker(Func<bool> shouldBlock, Func<bool> shouldBlockEscape, Faults faults)
    {
        this.shouldBlock = shouldBlock;
        this.shouldBlockEscape = shouldBlockEscape;
        this.faults = faults;

        // Each address is read in its own lambda, so a ClientStructs member that's gone throws inside Hook's try.
        longPressHook = Hook(
            () => (nint)InputData.MemberFunctionPointers.IsInputIdHeld,
            LongPressDetour,
            "IsInputIdHeld"
        );
        pressedHook = Hook(
            () => (nint)InputData.MemberFunctionPointers.IsInputIdPressed,
            PressedDetour,
            "IsInputIdPressed"
        );
        downHook = Hook(() => (nint)InputData.MemberFunctionPointers.IsInputIdDown, DownDetour, "IsInputIdDown");
        releasedHook = Hook(
            () => (nint)InputData.MemberFunctionPointers.IsInputIdReleased,
            ReleasedDetour,
            "IsInputIdReleased"
        );

        mouseWheelHook = HookBySignature<GetMouseWheelDelegate>(
            MouseWheelSignature,
            MouseWheelDetour,
            "getMouseWheelStatus"
        );
    }

    /// <summary>True when all four input queries resolved and hooked.</summary>
    public bool QueriesHooked => Hooks.All(hook => hook is not null);

    /// <summary>True when the mouse wheel reader resolved and hooked.</summary>
    public bool WheelHooked => mouseWheelHook is not null;

    /// <summary>Scans for a function and hooks it. ScanText already follows a call or jmp match.</summary>
    private static Hook<T>? HookBySignature<T>(string signature, T detour, string name)
        where T : Delegate
    {
        nint address;
        try
        {
            address = Plugin.SigScanner.ScanText(signature);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error("[input] {Name} signature did not resolve: {Message}", name, ex.Message);
            return null;
        }

        Plugin.Log.Debug("[input] {Name} resolved to 0x{Addr:X}", name, address);
        return Install(address, detour, name);
    }

    private static Hook<IsInputIdDelegate>? Hook(Func<nint> address, IsInputIdDelegate detour, string name)
    {
        nint resolved;
        try
        {
            resolved = address();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[input] {Name} address did not resolve; not hooked.", name);
            return null;
        }

        if (resolved == 0)
        {
            Plugin.Log.Error("[input] {Name} address did not resolve; not hooked.", name);
            return null;
        }

        // Left disabled. These fire thousands of times a second, so they are only
        // enabled while we are flying.
        return Install(resolved, detour, name);
    }

    /// <summary>Creates a hook at <paramref name="address"/>, left disabled, or null if it can't be.</summary>
    private static Hook<T>? Install<T>(nint address, T detour, string name)
        where T : Delegate
    {
        try
        {
            return Plugin.Hooks.HookFromAddress<T>(address, detour);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[input] {Name} did not hook.", name);
            return null;
        }
    }

    private byte LongPressDetour(InputData* self, InputId id) => Filter(longPressHook!, self, id);

    private byte PressedDetour(InputData* self, InputId id) => Filter(pressedHook!, self, id);

    private byte DownDetour(InputData* self, InputId id) => Filter(downHook!, self, id);

    private byte ReleasedDetour(InputData* self, InputId id) => Filter(releasedHook!, self, id);

    /// <summary>Zoom. Suppressed while flying so the camera distance is left alone.</summary>
    private sbyte MouseWheelDetour()
    {
        // Always call through, then discard. Skipping the original could leave the
        // wheel delta unconsumed for the next reader.
        var value = mouseWheelHook!.Original();
        if (faults.Any)
            return value;
        try
        {
            return shouldBlock() ? (sbyte)0 : value;
        }
        catch (Exception ex)
        {
            faults.Record("mouse wheel hook", ex);
            return value;
        }
    }

    private byte Filter(Hook<IsInputIdDelegate> hook, InputData* self, InputId id)
    {
        if (faults.Any)
            return hook.Original(self, id);
        try
        {
            if (Blocks(id))
                return 0;
        }
        catch (Exception ex)
        {
            faults.Record("input query hooks", ex);
        }

        return hook.Original(self, id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Blocks(InputId id) =>
        shouldBlock() && (Blocked.Contains(id) || (id == InputId.ESC && shouldBlockEscape()));

    /// <summary>Enables the hooks only while they can do something. Call every frame.</summary>
    public void SyncHookState()
    {
        var wanted = shouldBlock();
        foreach (var hook in Hooks)
            Sync(hook, wanted);
        Sync(mouseWheelHook, wanted);
    }

    /// <summary>Enables or disables <paramref name="hook"/> to match <paramref name="wanted"/>, if it was made.</summary>
    private static void Sync<T>(Hook<T>? hook, bool wanted)
        where T : Delegate
    {
        if (hook is null)
            return;
        if (wanted && !hook.IsEnabled)
            hook.Enable();
        else if (!wanted && hook.IsEnabled)
            hook.Disable();
    }

    /// <summary>Disables every hook until the next <see cref="SyncHookState"/> wants them.</summary>
    public void DisableHooks()
    {
        foreach (var hook in Hooks)
            hook?.Disable();
        mouseWheelHook?.Disable();
    }

    private IEnumerable<Hook<IsInputIdDelegate>?> Hooks
    {
        get
        {
            yield return longPressHook;
            yield return pressedHook;
            yield return downHook;
            yield return releasedHook;
        }
    }

#if DEBUG
    /// <summary>How many of the five hooks, four queries and the wheel, are enabled.</summary>
    public (int Enabled, int Total) SelfTestHookState()
    {
        var all = Hooks.Cast<IDalamudHook?>().Append(mouseWheelHook).ToList();
        return (all.Count(hook => hook is { IsEnabled: true }), all.Count);
    }
#endif

    public void Dispose()
    {
        foreach (var hook in Hooks)
            Remove(hook);
        Remove(mouseWheelHook);
    }

    /// <summary>Disables and disposes <paramref name="hook"/>, if it was made.</summary>
    private static void Remove<T>(Hook<T>? hook)
        where T : Delegate
    {
        hook?.Disable();
        hook?.Dispose();
    }
}
