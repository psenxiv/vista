using System.Collections.Concurrent;
using Vista.Core.Session;

namespace Vista.Plugin.Game;

/// <summary>Exceptions caught at Vista's entry points, from any thread, waiting for the framework update to stop Vista.</summary>
internal sealed class Faults(Func<CameraMode> mode)
{
    private readonly ConcurrentQueue<Fault> waiting = new();
    private volatile bool any;

    /// <summary>One caught exception: where it happened, and whether the player is told if it's the first stop.</summary>
    public readonly record struct Fault(string Where, bool Notifies);

    /// <summary>True once any fault is recorded; hooks then leave the game alone until the plugin is reloaded.</summary>
    public bool Any => any;

    /// <summary>Logs <paramref name="exception"/> with where it happened and the mode, and queues it for the framework update.</summary>
    public void Record(string where, Exception exception, bool notifies = true)
    {
        any = true;
        Plugin.Log.Error(exception, "[vista] fault in {Where} while {Mode}", where, mode());
        waiting.Enqueue(new Fault(where, notifies));
    }

    /// <summary>Takes the next waiting fault, oldest first.</summary>
    public bool TryTake(out Fault fault) => waiting.TryDequeue(out fault);

    /// <summary>Runs <paramref name="step"/>, recording a fault at <paramref name="where"/> if it throws.</summary>
    public void Guard(string where, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            Record(where, ex);
        }
    }

    /// <summary>Runs one step of a sequence that must finish, logging it by name if it throws so the rest still run.</summary>
    public static void Attempt(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[vista] {Step} failed", name);
        }
    }
}
