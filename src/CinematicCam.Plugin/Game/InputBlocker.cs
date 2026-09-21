using System.Collections.Frozen;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace CinematicCam.Plugin.Game;

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
        InputId.MOVE_FORE, InputId.MOVE_BACK,
        InputId.MOVE_LEFT, InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L, InputId.MOVE_STRIFE_R,
        InputId.JUMP, InputId.MOVE_DESCENT, InputId.MOVE_RETENTION,
    }.ToFrozenSet();

    private readonly Hook<IsInputIdDelegate>? longPressHook;
    private readonly Hook<IsInputIdDelegate>? pressedHook;
    private readonly Hook<IsInputIdDelegate>? downHook;
    private readonly Hook<IsInputIdDelegate>? releasedHook;
    private readonly Hook<GetMouseWheelDelegate>? mouseWheelHook;

    private readonly Func<bool> shouldBlock;
    private readonly Func<bool> shouldBlockEscape;

    public InputBlocker(Func<bool> shouldBlock, Func<bool> shouldBlockEscape)
    {
        this.shouldBlock = shouldBlock;
        this.shouldBlockEscape = shouldBlockEscape;

        longPressHook = Hook(InputData.MemberFunctionPointers.IsInputIdHeld, LongPressDetour, "IsInputIdHeld");
        pressedHook = Hook(InputData.MemberFunctionPointers.IsInputIdPressed, PressedDetour, "IsInputIdPressed");
        downHook = Hook(InputData.MemberFunctionPointers.IsInputIdDown, DownDetour, "IsInputIdDown");
        releasedHook = Hook(InputData.MemberFunctionPointers.IsInputIdReleased, ReleasedDetour, "IsInputIdReleased");

        mouseWheelHook = HookBySignature<GetMouseWheelDelegate>(MouseWheelSignature, MouseWheelDetour, "getMouseWheelStatus");
    }

    /// <summary>Scans for a function and hooks it. ScanText already follows a call or jmp match.</summary>
    private static Hook<T>? HookBySignature<T>(string signature, T detour, string name) where T : Delegate
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

        Plugin.Log.Information("[input] {Name} resolved to 0x{Addr:X}", name, address);
        return Plugin.Hooks.HookFromAddress<T>(address, detour);
    }

    private static Hook<IsInputIdDelegate>? Hook(void* address, IsInputIdDelegate detour, string name)
    {
        if (address == null)
        {
            Plugin.Log.Error("[input] {Name} address did not resolve; not hooked.", name);
            return null;
        }

        // Left disabled. These fire thousands of times a second, so they are only
        // enabled while we are flying.
        return Plugin.Hooks.HookFromAddress<IsInputIdDelegate>((nint)address, detour);
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
        return shouldBlock() ? (sbyte)0 : value;
    }

    private byte Filter(Hook<IsInputIdDelegate> hook, InputData* self, InputId id)
        => shouldBlock() && (Blocked.Contains(id) || (id == InputId.ESC && shouldBlockEscape()))
            ? (byte)0
            : hook.Original(self, id);

    /// <summary>Enables the hooks only while they can do something. Call every frame.</summary>
    public void SyncHookState()
    {
        var wanted = shouldBlock();

        foreach (var hook in Hooks)
        {
            if (hook is null) continue;
            if (wanted && !hook.IsEnabled) hook.Enable();
            else if (!wanted && hook.IsEnabled) hook.Disable();
        }

        if (mouseWheelHook is null) return;
        if (wanted && !mouseWheelHook.IsEnabled) mouseWheelHook.Enable();
        else if (!wanted && mouseWheelHook.IsEnabled) mouseWheelHook.Disable();
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

    public void Dispose()
    {
        foreach (var hook in Hooks)
        {
            hook?.Disable();
            hook?.Dispose();
        }

        mouseWheelHook?.Disable();
        mouseWheelHook?.Dispose();
    }
}
