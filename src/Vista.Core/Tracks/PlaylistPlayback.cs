using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>One playlist entry ready to play: its entry, its track in the world, and how many times (null follows the track).</summary>
public sealed record PlaylistItem(Guid EntryId, Track Track, int? Loops);

/// <summary>Plays playlist entries in turn with a cut between them, carrying time over, and holds the last frame at the end.</summary>
public sealed class PlaylistPlayback : IPlayback
{
    private readonly IReadOnlyList<PlaylistItem> items;
    private readonly TrackEvaluator[] evaluators;
    private double clock;

    /// <summary>Plays <paramref name="items"/> from the first; refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items)
    {
        if (items.Count == 0) throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
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

    /// <summary>Moves on by <paramref name="dt"/>, cutting to later entries as earlier ones finish, and returns the frame.</summary>
    public CameraState? Advance(float dt)
    {
        if (!IsFinished) clock += Math.Max(dt, 0f);

        while (!IsFinished && clock >= Total && (Total > 0 || clock > 0))
        {
            if (Index == items.Count - 1)
            {
                clock = Total;
                IsFinished = true;
                break;
            }

            clock -= Total;
            Index++;

            // A zero-length entry is shown for the frame it's reached on.
            if (Total == 0)
            {
                clock = 0;
                break;
            }
        }

        return evaluators[Index].Evaluate(ShotTime);
    }

    /// <summary>Jumps to <paramref name="time"/> in the playing entry, staying in the loop pass it is on.</summary>
    public void Seek(double time)
    {
        var pass = PassClock;
        var onReturn = PlaybackClock.OnReturnPass(Direction, ShotLength, pass);
        clock = clock - pass + PlaybackClock.ClockFor(Direction, ShotLength, time, onReturn);
        IsFinished = Index == items.Count - 1 && clock >= Total;
    }

    /// <summary>Goes back to the first entry's start.</summary>
    public void Restart()
    {
        Index = 0;
        clock = 0;
        IsFinished = false;
    }

    private PlaybackDirection Direction => items[Index].Track.Direction;

    private double Cycle => PlaybackClock.CycleLength(Direction, ShotLength);

    /// <summary>How long the playing entry plays: N cycles, one cycle, or for good when its track loops with no count.</summary>
    private double Total => items[Index].Loops is { } n ? n * Cycle : items[Index].Track.Loop ? double.PositiveInfinity : Cycle;

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
