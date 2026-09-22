using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Advances a track's playback clock frame by frame, by its direction and loop setting.</summary>
public sealed class TrackPlayback : IPlayback
{
    private readonly Track _track;
    private readonly TrackEvaluator _evaluator;
    private double _clock;

    /// <summary>Where the camera is in the shot, from 0 to <see cref="TrackEvaluator.Duration"/>.</summary>
    public double ShotTime => PlaybackClock.ShotTime(_track.Direction, _evaluator.Duration, _clock);

    /// <summary>The track's length in seconds.</summary>
    public double ShotLength => _evaluator.Duration;

    /// <summary>True once a track that doesn't loop has reached the end of its cycle; never true for one that loops.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Starts <paramref name="track"/> at the start of its cycle.</summary>
    public TrackPlayback(Track track)
    {
        _track = track;
        _evaluator = new TrackEvaluator(track);
    }

    /// <summary>Adds <paramref name="dt"/> to the clock, stops or wraps it at the end of the cycle, and evaluates the shot time.</summary>
    public CameraState? Advance(float dt)
    {
        var cycle = Cycle;
        var next = _clock + Math.Max(dt, 0f);

        if (_track.Loop)
        {
            _clock = cycle > 0.0 ? next % cycle : 0.0;
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

        return _evaluator.Evaluate(ShotTime);
    }

    /// <summary>Puts the clock back to the start of the cycle and clears <see cref="IsFinished"/>.</summary>
    public void Restart()
    {
        _clock = 0.0;
        IsFinished = false;
    }

    /// <summary>Jumps to shot time <paramref name="time"/>, clamped to the shot, keeping a Ping-pong shot's pass.</summary>
    public void Seek(double time)
    {
        var length = _evaluator.Duration;
        var cycle = Cycle;
        var onReturn = PlaybackClock.OnReturnPass(_track.Direction, length, _clock);
        var clock = PlaybackClock.ClockFor(_track.Direction, length, time, onReturn);

        if (_track.Loop)
        {
            _clock = cycle > 0.0 ? clock % cycle : 0.0;
            return;
        }

        _clock = clock;
        IsFinished = _clock >= cycle;
    }

    private double Cycle => PlaybackClock.CycleLength(_track.Direction, _evaluator.Duration);
}
