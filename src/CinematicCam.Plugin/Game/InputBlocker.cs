using System.Collections.Frozen;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace CinematicCam.Plugin.Game;

/// <summary>Swallows movement inputs while the plugin flies the camera.</summary>
internal sealed unsafe class InputBlocker : IDisposable
{
    private delegate byte IsInputIdDelegate(InputData* self, uint inputId);
    private delegate sbyte GetMouseWheelDelegate();

    // FFXIVClientStructs does not map these two. The "held" query in particular is
    // missing: what CS calls IsInputIdHeld is really the long-press function, which
    // is why holding a movement key never reached our hooks.
    private const string HeldSignature = "E9 ?? ?? ?? ?? B9 4F 01 00 00";
    private const string MouseWheelSignature = "E8 ?? ?? ?? ?? F7 D8 48 8B CB";

    private readonly Hook<IsInputIdDelegate>? heldHook;
    private readonly Hook<IsInputIdDelegate>? pressedHook;
    private readonly Hook<IsInputIdDelegate>? downHook;
    private readonly Hook<IsInputIdDelegate>? releasedHook;
    private readonly Hook<IsInputIdDelegate>? heldHookReal;
    private readonly Hook<GetMouseWheelDelegate>? mouseWheelHook;

    private readonly Func<bool> shouldBlock;

    private readonly Lock seenLock = new();
    private readonly HashSet<uint> seen = [];

    // Read from the detour on every input query, replaced wholesale rather than
    // mutated, so the hot path needs no lock.
    private volatile FrozenSet<uint> blocked = FrozenSet<uint>.Empty;

    private bool learning;
    private bool loggedWheelUp;
    private bool loggedWheelDown;

    /// <summary>Input ids suppressed while flying.</summary>
    public IReadOnlyCollection<uint> Blocked => blocked;

    /// <summary>Records which input ids the game reports as active, and blocks nothing.</summary>
    public bool Learning
    {
        get => learning;
        set
        {
            // Clear when starting, or ids seen in the gap are recorded then discarded.
            // Never on stop, or the results are wiped before anything can read them.
            if (value)
            {
                lock (seenLock) seen.Clear();
                loggedWheelUp = loggedWheelDown = false;
            }

            learning = value;
            SyncHookState();
        }
    }

    public InputBlocker(Func<bool> shouldBlock)
    {
        this.shouldBlock = shouldBlock;

        heldHook = Hook(InputData.MemberFunctionPointers.IsInputIdHeld, HeldDetour, "IsInputIdHeld");
        pressedHook = Hook(InputData.MemberFunctionPointers.IsInputIdPressed, PressedDetour, "IsInputIdPressed");
        downHook = Hook(InputData.MemberFunctionPointers.IsInputIdDown, DownDetour, "IsInputIdDown");
        releasedHook = Hook(InputData.MemberFunctionPointers.IsInputIdReleased, ReleasedDetour, "IsInputIdReleased");

        heldHookReal = HookBySignature<IsInputIdDelegate>(HeldSignature, HeldRealDetour, "isInputIdHeld");
        mouseWheelHook = HookBySignature<GetMouseWheelDelegate>(MouseWheelSignature, MouseWheelDetour, "getMouseWheelStatus");
    }

    /// <summary>Scans for a function, following the relative target when the match is a call or jmp.</summary>
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

        var opcode = *(byte*)address;
        if (opcode is 0xE8 or 0xE9)
        {
            var relative = *(int*)(address + 1);
            address = address + 5 + relative;
        }

        Plugin.Log.Information("[input] {Name} resolved to 0x{Addr:X}", name, address);

        var hook = Plugin.Hooks.HookFromAddress<T>(address, detour);
        return hook;
    }

    private static Hook<IsInputIdDelegate>? Hook(void* address, IsInputIdDelegate detour, string name)
    {
        if (address == null)
        {
            Plugin.Log.Error("[input] {Name} address did not resolve; not hooked.", name);
            return null;
        }

        // Left disabled. These fire thousands of times a second, so they are only
        // enabled while there is something to block or learn.
        return Plugin.Hooks.HookFromAddress<IsInputIdDelegate>((nint)address, detour);
    }

    private byte HeldDetour(InputData* self, uint id) => Filter(heldHook!, self, id);
    private byte PressedDetour(InputData* self, uint id) => Filter(pressedHook!, self, id);
    private byte DownDetour(InputData* self, uint id) => Filter(downHook!, self, id);
    private byte ReleasedDetour(InputData* self, uint id) => Filter(releasedHook!, self, id);
    private byte HeldRealDetour(InputData* self, uint id) => Filter(heldHookReal!, self, id);

    /// <summary>Zoom. Suppressed while flying so the camera distance is left alone.</summary>
    private sbyte MouseWheelDetour()
    {
        var value = mouseWheelHook!.Original();

        // The wheel fires several times per notch; log each direction once.
        if (learning && value > 0 && !loggedWheelUp)
        {
            loggedWheelUp = true;
            Plugin.Log.Information("[input] mouse wheel up seen");
        }
        else if (learning && value < 0 && !loggedWheelDown)
        {
            loggedWheelDown = true;
            Plugin.Log.Information("[input] mouse wheel down seen");
        }

        return shouldBlock() && !learning ? (sbyte)0 : value;
    }

    private byte Filter(Hook<IsInputIdDelegate> hook, InputData* self, uint id)
    {
        var active = hook.Original(self, id);
        if (active == 0) return 0;

        if (learning)
        {
            bool isNew;
            lock (seenLock) isNew = seen.Add(id);

            if (isNew) Plugin.Log.Information("[input] active id {Id}", id);
            return active;
        }

        return shouldBlock() && blocked.Contains(id) ? (byte)0 : active;
    }

    /// <summary>Replaces the blocked set with everything learning observed.</summary>
    public void BlockWhatWasLearned()
    {
        uint[] ids;
        lock (seenLock) ids = [.. seen];

        blocked = ids.ToFrozenSet();
        Plugin.Log.Information("[input] blocking {Count} ids: {Ids}", ids.Length, string.Join(", ", ids.Order()));
        SyncHookState();
    }

    public void ClearBlocked()
    {
        blocked = FrozenSet<uint>.Empty;
        Plugin.Log.Information("[input] blocked set cleared.");
        SyncHookState();
    }

    /// <summary>Enables the hooks only while they can do something. Call every frame.</summary>
    public void SyncHookState()
    {
        var wanted = learning || (blocked.Count > 0 && shouldBlock());

        foreach (var hook in Hooks)
        {
            if (hook is null) continue;
            if (wanted && !hook.IsEnabled) hook.Enable();
            else if (!wanted && hook.IsEnabled) hook.Disable();
        }

        // Zoom is suppressed whenever we hold the camera, with no id to learn first.
        var wheelWanted = learning || shouldBlock();
        if (mouseWheelHook is not null)
        {
            if (wheelWanted && !mouseWheelHook.IsEnabled) mouseWheelHook.Enable();
            else if (!wheelWanted && mouseWheelHook.IsEnabled) mouseWheelHook.Disable();
        }
    }

    private IEnumerable<Hook<IsInputIdDelegate>?> Hooks
    {
        get
        {
            yield return heldHook;
            yield return pressedHook;
            yield return downHook;
            yield return releasedHook;
            yield return heldHookReal;
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
