namespace Vista.Plugin.Game;

/// <summary>Stops the character moving, without touching input.</summary>
internal sealed unsafe class MovementLock : IDisposable
{
    // Static int the game treats as a reference count: non-zero disables movement.
    // Signature from Hypostasis, which Cammy uses for the same purpose. The instruction
    // loads a float; the counter is the next dword, so the 4 applies to the resolved
    // address, not to the scan. Passing it to the scanner decodes mid-instruction.
    private const string Signature = "F3 0F 10 05 ?? ?? ?? ?? 0F 2E C7";
    private const int CounterOffset = 4;

    private readonly int* counter;

    public bool Held { get; private set; }
    public bool Available => counter != null;

    /// <summary>The game's current count. Other plugins share it.</summary>
    public int Count => counter != null ? *counter : 0;

    public MovementLock()
    {
        nint address;
        try
        {
            address = Plugin.SigScanner.GetStaticAddressFromSig(Signature);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error("[movement] lock signature did not resolve: {Message}", ex.Message);
            return;
        }

        if (address == 0)
        {
            Plugin.Log.Error("[movement] lock signature resolved to zero; movement will not lock.");
            return;
        }

        counter = (int*)(address + CounterOffset);
        Plugin.Log.Debug("[movement] lock counter at 0x{Addr:X}", (nint)counter);
    }

    public void Hold()
    {
        if (Held || counter == null) return;

        (*counter)++;
        Held = true;
        Plugin.Log.Debug("[movement] disabled, counter now {Count}", *counter);
    }

    public void Release()
    {
        if (!Held || counter == null) return;

        // Decrement rather than zero it: other plugins share this counter.
        if (*counter > 0) (*counter)--;
        Held = false;
        Plugin.Log.Debug("[movement] enabled, counter now {Count}", *counter);
    }

    /// <summary>Drops our hold without decrementing, for when something else cleared the counter.</summary>
    public void Forget()
    {
        if (!Held) return;
        Held = false;
        Plugin.Log.Warning("[movement] counter cleared elsewhere; dropped our hold without decrementing.");
    }

    public void Dispose() => Release();
}
