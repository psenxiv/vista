using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Tracks.Playback;

/// <summary>One playlist entry ready to play: its entry, its track in the world, and how many times (null follows the track).</summary>
public sealed record PlaylistItem(Guid EntryId, Track Track, int? Loops);

/// <summary>Plays playlist entries in turn with a cut between them, carrying time over; at the end holds the last frame, or wraps to the first entry when looping.</summary>
public sealed class PlaylistPlayback : IPlayback
{
    private readonly IReadOnlyList<PlaylistItem> items;
    private readonly TrackEvaluator[] evaluators;
    private readonly bool loops;
    private readonly AimTracker aim;
    private double clock;
    private bool shownFirstFrame;

    /// <summary>Plays <paramref name="items"/> from the first, wrapping at the end when <paramref name="loops"/>; <paramref name="targets"/> finds watched or followed characters. Refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false, NearbyCharacters? targets = null)
    {
        if (items.Count == 0) throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        this.loops = loops;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
        aim = new AimTracker(targets);
    }

    /// <summary>The index of the entry playing, among the items.</summary>
    public int Index { get; private set; }

    /// <summary>The Id of the entry playing.</summary>
    public Guid EntryId => items[Index].EntryId;

    /// <summary>True once the last entry has finished; its last frame holds.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>The playing entry's track length in seconds.</summary>
    public double ShotLength => evaluators[Index].Duration;

    /// <summary>Where the camera is in the playing entry's track.</summary>
    public double ShotTime => PlaybackClock.ShotTime(Direction, ShotLength, PassClock);

    /// <summary>Moves on by <paramref name="dt"/>, cutting to later entries as earlier ones finish, and returns the frame; a cut starts the smoothing afresh.</summary>
    public CameraState? Advance(float dt)
    {
        // A zero-length first entry gets its own frame before time starts moving.
        if (Index == 0 && !shownFirstFrame)
        {
            shownFirstFrame = true;
            if (Total == 0) return Frame(dt);
        }

        if (!IsFinished) clock += Math.Max(dt, 0f);

        var wrapped = false;
        var cut = false;
        while (!IsFinished && clock >= Total && (Total > 0 || clock > 0))
        {
            if (Index == items.Count - 1)
            {
                if (!loops)
                {
                    clock = Total;
                    IsFinished = true;
                    break;
                }

                // One wrap per Advance, so a very long frame or a playlist with no length never spins.
                if (wrapped)
                {
                    Index = 0;
                    clock = 0;
                    cut = true;
                    break;
                }

                wrapped = true;
                clock -= Total;
                Index = 0;
                cut = true;
            }
            else
            {
                clock -= Total;
                Index++;
                cut = true;
            }

            // A zero-length entry is shown for the frame it's reached on.
            if (Total == 0)
            {
                clock = 0;
                break;
            }
        }

        if (cut) aim.Reset();
        return Frame(dt);
    }

    /// <summary>The playing entry's frame now, aimed at its target.</summary>
    private CameraState? Frame(float dt) => aim.Frame(evaluators[Index], items[Index].Track, ShotTime, Math.Max(dt, 0f));

    /// <summary>Jumps to <paramref name="time"/> in the playing entry, staying in the loop pass it is on; the smoothing starts afresh.</summary>
    public void Seek(double time)
    {
        var pass = PassClock;
        var onReturn = PlaybackClock.OnReturnPass(Direction, ShotLength, pass);
        clock = clock - pass + PlaybackClock.ClockFor(Direction, ShotLength, time, onReturn);
        IsFinished = !loops && Index == items.Count - 1 && clock >= Total;
        aim.Reset();
    }

    /// <summary>Goes back to the first entry's start; the smoothing starts afresh.</summary>
    public void Restart()
    {
        Index = 0;
        clock = 0;
        IsFinished = false;
        shownFirstFrame = false;
        aim.Reset();
    }

    private PlaybackDirection Direction => items[Index].Track.Direction;

    private double Cycle => PlaybackClock.CycleLength(Direction, ShotLength);

    /// <summary>How long the playing entry plays: N cycles (never fewer than one), one cycle, or for good when its track loops with no count.</summary>
    private double Total => items[Index].Loops is { } n ? Math.Max(n, 1) * Cycle : items[Index].Track.Loop ? double.PositiveInfinity : Cycle;

    /// <summary>The clock within the loop pass the playing entry is on; a finished entry sits at the end of its last pass.</summary>
    private double PassClock
    {
        get
        {
            var cycle = Cycle;
            if (cycle <= 0) return 0;
            if (!double.IsInfinity(Total) && clock >= Total) return cycle;
            return clock % cycle;
        }
    }
}
