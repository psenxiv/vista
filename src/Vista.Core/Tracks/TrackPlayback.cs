using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Advances a track's elapsed time frame by frame according to its playback mode.</summary>
public sealed class TrackPlayback
{
    private readonly Track _track;
    private readonly TrackEvaluator _evaluator;

    /// <summary>Seconds into the track. Clamps at <see cref="TrackEvaluator.Duration"/>, or wraps modulo it when the track loops.</summary>
    public double Elapsed { get; private set; }

    /// <summary>True once a track that doesn't loop has reached its duration; never true for one that loops.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Starts <paramref name="track"/> at elapsed zero.</summary>
    public TrackPlayback(Track track)
    {
        _track = track;
        _evaluator = new TrackEvaluator(track);
    }

    /// <summary>Adds <paramref name="dt"/> to elapsed time, applies the track's playback mode, and evaluates the result.</summary>
    public CameraState? Advance(float dt)
    {
        var duration = _evaluator.Duration;
        var next = Elapsed + Math.Max(dt, 0f);

        if (_track.Loop)
        {
            Elapsed = duration > 0.0 ? next % duration : 0.0;
        }
        else if (next >= duration)
        {
            Elapsed = duration;
            IsFinished = true;
        }
        else
        {
            Elapsed = next;
        }

        return _evaluator.Evaluate(Elapsed);
    }

    /// <summary>Resets elapsed time to zero and clears <see cref="IsFinished"/>.</summary>
    public void Restart()
    {
        Elapsed = 0.0;
        IsFinished = false;
    }

    /// <summary>Jumps to <paramref name="time"/>: clamps to the track and finishes at its end, or wraps when the track loops.</summary>
    public void Seek(double time)
    {
        var duration = _evaluator.Duration;
        if (_track.Loop)
        {
            Elapsed = duration > 0.0 ? ((time % duration) + duration) % duration : 0.0;
            return;
        }

        Elapsed = Math.Clamp(time, 0.0, duration);
        IsFinished = Elapsed >= duration;
    }
}
