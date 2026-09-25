using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Tracks.Playback;

/// <summary>Advances a track's playback clock frame by frame, by its direction and loop setting.</summary>
public sealed class TrackPlayback : IPlayback
{
    private readonly Track _track;
    private readonly TrackEvaluator _evaluator;
    private readonly AimTracker aim;
    private double _clock;

    /// <summary>Where the camera is in the shot, from 0 to <see cref="TrackEvaluator.Duration"/>.</summary>
    public double ShotTime => PlaybackClock.ShotTime(_track.Direction, _evaluator.Duration, _clock);

    /// <summary>The track's length in seconds.</summary>
    public double ShotLength => _evaluator.Duration;

    /// <summary>True once a track that doesn't loop has reached the end of its cycle; never true for one that loops.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Starts <paramref name="track"/>, a track in the world, at the start of its cycle; <paramref name="targets"/> finds a watched or followed character.</summary>
    public TrackPlayback(Track track, NearbyCharacters? targets = null)
    {
        _track = track;
        _evaluator = new TrackEvaluator(track);
        aim = new AimTracker(targets);
    }

    /// <summary>Adds <paramref name="dt"/> to the clock, stops or wraps it at the end of the cycle, and evaluates the shot time.</summary>
    public CameraState? Advance(float dt)
    {
        var cycle = Cycle;
        var next = _clock + Math.Max(dt, 0f);

        if (_track.Loop)
        {
            _clock = PlaybackClock.Wrap(next, cycle);
        }
        else if (next >= cycle)
        {
            _clock = cycle;
            IsFinished = true;
        }
        else
        {
            _clock = next;
        }

        return aim.Frame(_evaluator, _track, ShotTime, Math.Max(dt, 0f));
    }

    /// <summary>Puts the clock back to the start of the cycle, clears <see cref="IsFinished"/> and starts the smoothing afresh.</summary>
    public void Restart()
    {
        aim.Reset();
        _clock = 0.0;
        IsFinished = false;
    }

    /// <summary>Jumps to shot time <paramref name="time"/>, clamped to the shot, keeping a Ping-pong shot's pass; the smoothing starts afresh.</summary>
    public void Seek(double time)
    {
        aim.Reset();
        var length = _evaluator.Duration;
        var cycle = Cycle;
        var onReturn = PlaybackClock.OnReturnPass(_track.Direction, length, _clock);
        var clock = PlaybackClock.ClockFor(_track.Direction, length, time, onReturn);

        if (_track.Loop)
        {
            _clock = PlaybackClock.Wrap(clock, cycle);
            return;
        }

        _clock = clock;
        IsFinished = _clock >= cycle;
    }

    private double Cycle => PlaybackClock.CycleLength(_track.Direction, _evaluator.Duration);
}
