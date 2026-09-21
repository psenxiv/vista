using System.Collections.Frozen;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Game;

/// <summary>Stops flight keys firing their usual actions while the plugin flies the camera.</summary>
internal sealed unsafe class InputBlocker : IDisposable
{
    private delegate byte IsInputIdDelegate(InputData* self, InputId inputId);
    private delegate sbyte GetMouseWheelDelegate();

    // FFXIVClientStructs does not map these two. The held query in particular is missing:
    // what CS calls IsInputIdHeld is really the long-press function.
    private const string HeldSignature = "E9 ?? ?? ?? ?? B9 4F 01 00 00";
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
    private readonly Hook<IsInputIdDelegate>? heldHook;
    private readonly Hook<GetMouseWheelDelegate>? mouseWheelHook;

    private readonly Func<bool> shouldBlock;

    public InputBlocker(Func<bool> shouldBlock)
    {
        this.shouldBlock = shouldBlock;

        longPressHook = Hook(InputData.MemberFunctionPointers.IsInputIdHeld, LongPressDetour, "IsInputIdHeld");
        pressedHook = Hook(InputData.MemberFunctionPointers.IsInputIdPressed, PressedDetour, "IsInputIdPressed");
        downHook = Hook(InputData.MemberFunctionPointers.IsInputIdDown, DownDetour, "IsInputIdDown");
        releasedHook = Hook(InputData.MemberFunctionPointers.IsInputIdReleased, ReleasedDetour, "IsInputIdReleased");

        heldHook = HookBySignature<IsInputIdDelegate>(HeldSignature, HeldDetour, "isInputIdHeld");
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
    private byte HeldDetour(InputData* self, InputId id) => Filter(heldHook!, self, id);

    /// <summary>Zoom. Suppressed while flying so the camera distance is left alone.</summary>
    private sbyte MouseWheelDetour()
        => shouldBlock() ? (sbyte)0 : mouseWheelHook!.Original();

    private byte Filter(Hook<IsInputIdDelegate> hook, InputData* self, InputId id)
        => shouldBlock() && Blocked.Contains(id) ? (byte)0 : hook.Original(self, id);

    /// <summary>True while the player holds that bind, read past our own block.</summary>
    public bool IsHeld(InputId id)
    {
        if (heldHook is null) return false;

        var input = Input();
        return input != null && heldHook.Original(input, id) != 0;
    }

    private static InputData* Input()
    {
        var ui = UIInputData.Instance();
        return ui == null ? null : &ui->InputData;
    }

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
            yield return heldHook;
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
