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

    /// <summary>Adds <paramref name="dt"/> to the clock, stops it at the end of the cycle or wraps it once it moves past, and evaluates the shot time.</summary>
    public CameraState? Advance(float dt)
    {
        var cycle = Cycle;
        var step = Math.Max(dt, 0f);
        var next = _clock + step;

        if (_track.Loop)
        {
            if (step > 0f)
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

        return aim.Frame(_evaluator, _track, ShotTime, step);
    }

    /// <summary>Puts the clock back to the start of the cycle, clears <see cref="IsFinished"/> and starts the smoothing afresh.</summary>
    public void Restart()
    {
        aim.Reset();
        _clock = 0.0;
        IsFinished = false;
    }

    /// <summary>Jumps to shot time <paramref name="time"/>, clamped to the shot, keeping a Ping-pong shot's pass; a seek to the end of a looping cycle stays there until the clock moves on. The smoothing starts afresh.</summary>
    public void Seek(double time)
    {
        aim.Reset();
        var length = _evaluator.Duration;
        var onReturn = PlaybackClock.OnReturnPass(_track.Direction, length, _clock);
        _clock = PlaybackClock.ClockFor(_track.Direction, length, time, onReturn);
        IsFinished = !_track.Loop && _clock >= Cycle;
    }

    private double Cycle => PlaybackClock.CycleLength(_track.Direction, _evaluator.Duration);
}
