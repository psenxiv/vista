using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Tracks.Playback;

/// <summary>Plays playlist entries in turn with a cut between them, carrying time over; at the end holds the last frame, or wraps to the first entry when looping.</summary>
public sealed class PlaylistPlayback : IPlayback
{
    private readonly IReadOnlyList<PlaylistItem> items;
    private readonly TrackEvaluator[] evaluators;
    private readonly bool loops;
    private readonly AimTracker aim;
    private double clock;
    private bool shownFirstFrame;
    private bool atPassEnd;

    /// <summary>Plays <paramref name="items"/> from the first, wrapping at the end when <paramref name="loops"/>; <paramref name="targets"/> finds watched or followed characters. Refused when empty.</summary>
    public PlaylistPlayback(IReadOnlyList<PlaylistItem> items, bool loops = false, NearbyCharacters? targets = null)
    {
        if (items.Count == 0)
            throw new ArgumentException("A playlist needs an entry to play.");
        this.items = items;
        this.loops = loops;
        evaluators = items.Select(i => new TrackEvaluator(i.Track)).ToArray();
        Timeline = new PlaylistTimeline(items, evaluators.Select(e => e.Duration).ToArray(), loops);
        aim = new AimTracker(targets);
    }

    /// <summary>The playlist laid end to end, as the playhead crosses it.</summary>
    public PlaylistTimeline Timeline { get; }

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

    /// <summary>Where playback is: the playing entry, its loop pass, and the seconds into that pass; an entry looping forever stays on its first pass.</summary>
    public PlaylistPosition Position => new(Index, Pass, PassClock);

    /// <summary>Seconds through the playlist; over an entry looping forever it goes round that entry's segment.</summary>
    public double PlaylistTime => Timeline.TimeOf(Position);

    /// <summary>Moves on by <paramref name="dt"/>, cutting to later entries as earlier ones finish, and returns the frame; a cut starts the smoothing afresh.</summary>
    public CameraState? Advance(float dt)
    {
        // A zero-length first entry gets its own frame before time starts moving.
        if (Index == 0 && !shownFirstFrame)
        {
            shownFirstFrame = true;
            if (Total == 0)
                return Frame(dt);
        }

        var step = Math.Max(dt, 0f);
        if (!IsFinished)
            clock += step;
        if (step > 0f)
            atPassEnd = false;

        var wrapped = false;
        var cut = false;
        // A frame that doesn't move the clock never cuts, so a seek to an entry's end holds there.
        while (step > 0f && !IsFinished && clock >= Total)
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

        if (cut)
            aim.Reset();
        return Frame(dt);
    }

    /// <summary>The playing entry's frame now, aimed at its target.</summary>
    private CameraState? Frame(float dt) =>
        aim.Frame(evaluators[Index], items[Index].Track, ShotTime, Math.Max(dt, 0f));

    /// <summary>Jumps to <paramref name="time"/> in the playing entry, staying in the loop pass it is on; a seek to a pass's end stays there until the clock moves on. The smoothing starts afresh.</summary>
    public void Seek(double time)
    {
        var pass = PassClock;
        var onReturn = PlaybackClock.OnReturnPass(Direction, ShotLength, pass);
        var passClock = PlaybackClock.ClockFor(Direction, ShotLength, time, onReturn);
        clock = clock - pass + passClock;
        atPassEnd = passClock >= Cycle;
        IsFinished = !loops && Index == items.Count - 1 && clock >= Total;
        aim.Reset();
    }

    /// <summary>Cuts to the entry and time at <paramref name="time"/> through the playlist; the very end holds the last frame until the clock moves on. The smoothing starts afresh.</summary>
    public void SeekPlaylist(double time)
    {
        var position = Timeline.At(time);
        Index = position.Index;
        clock = (position.Pass * Cycle) + position.Time;
        atPassEnd = position.Time >= Cycle;
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
        atPassEnd = false;
        aim.Reset();
    }

    private PlaybackDirection Direction => items[Index].Track.Direction;

    private double Cycle => PlaybackClock.CycleLength(Direction, ShotLength);

    /// <summary>How long the playing entry plays: its segment's length, or for good when it loops forever.</summary>
    private double Total =>
        Timeline.Segments[Index] is { LoopsForever: false } segment ? segment.Length : double.PositiveInfinity;

    /// <summary>The loop pass the playing entry is on, from 0; a pass's end counts as that pass.</summary>
    private int Pass
    {
        get
        {
            var cycle = Cycle;
            if (cycle <= 0.0)
                return 0;
            var passes = Timeline.Segments[Index].Passes;
            return (int)Math.Clamp(Math.Round((clock - PassClock) / cycle), 0, passes - 1);
        }
    }

    /// <summary>The clock within the loop pass the playing entry is on; a finished entry, or one seeked to a pass's end, sits at that pass's end.</summary>
    private double PassClock
    {
        get
        {
            var cycle = Cycle;
            if (atPassEnd || (!double.IsInfinity(Total) && clock >= Total))
                return cycle;
            return PlaybackClock.Wrap(clock, cycle);
        }
    }
}
