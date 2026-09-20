namespace CinematicCam.Plugin.Game;

/// <summary>Stops the character moving, without touching input.</summary>
internal sealed unsafe class MovementLock : IDisposable
{
    // Static int the game treats as a reference count: non-zero disables movement.
    // Signature and offset from Hypostasis, which Cammy uses for the same purpose.
    private const string Signature = "F3 0F 10 05 ?? ?? ?? ?? 0F 2E C7";
    private const int SignatureOffset = 4;

    private readonly int* counter;

    public bool Held { get; private set; }
    public bool Available => counter != null;

    public MovementLock()
    {
        try
        {
            counter = (int*)Plugin.SigScanner.GetStaticAddressFromSig(Signature, SignatureOffset);
            Plugin.Log.Information("[movement] lock counter at 0x{Addr:X}", (nint)counter);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error("[movement] lock signature did not resolve: {Message}", ex.Message);
        }
    }

    public void Hold()
    {
        if (Held || counter == null) return;

        (*counter)++;
        Held = true;
        Plugin.Log.Information("[movement] disabled, counter now {Count}", *counter);
    }

    public void Release()
    {
        if (!Held || counter == null) return;

        // Decrement rather than zero it: other plugins share this counter.
        if (*counter > 0) (*counter)--;
        Held = false;
        Plugin.Log.Information("[movement] enabled, counter now {Count}", *counter);
    }

    public void Dispose() => Release();
}
