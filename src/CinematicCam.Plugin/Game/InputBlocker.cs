using System.Collections.Frozen;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace CinematicCam.Plugin.Game;

/// <summary>Swallows movement inputs while the plugin flies the camera.</summary>
internal sealed unsafe class InputBlocker : IDisposable
{
    private delegate byte IsInputIdDelegate(InputData* self, uint inputId);

    private readonly Hook<IsInputIdDelegate>? heldHook;
    private readonly Hook<IsInputIdDelegate>? pressedHook;
    private readonly Hook<IsInputIdDelegate>? downHook;
    private readonly Hook<IsInputIdDelegate>? releasedHook;

    private readonly Func<bool> shouldBlock;

    private readonly Lock seenLock = new();
    private readonly HashSet<uint> seen = [];

    // Read from the detour on every input query, replaced wholesale rather than
    // mutated, so the hot path needs no lock.
    private volatile FrozenSet<uint> blocked = FrozenSet<uint>.Empty;

    private bool learning;

    /// <summary>Input ids suppressed while flying.</summary>
    public IReadOnlyCollection<uint> Blocked => blocked;

    /// <summary>Records which input ids the game reports as active, and blocks nothing.</summary>
    public bool Learning
    {
        get => learning;
        set
        {
            // Clear before arming, or ids seen in the gap are recorded then discarded.
            lock (seenLock) seen.Clear();

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
    }

    private IEnumerable<Hook<IsInputIdDelegate>?> Hooks
    {
        get
        {
            yield return heldHook;
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
    }
}
