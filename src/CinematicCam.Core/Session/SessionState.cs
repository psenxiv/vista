using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome { Unchanged, FromOff, FromLive }

/// <summary>What <see cref="SessionState.Play"/> or <see cref="SessionState.Restart"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff }

/// <summary>The mode, the Director and the track, and the rules for moving between modes.</summary>
public sealed class SessionState
{
    public CameraMode Mode { get; private set; }

    public Director Director { get; } = new();

    /// <summary>The track Edit builds and Play plays. Changed only through <see cref="ChangeTrack"/>.</summary>
    public Track Track { get; private set; } = TrackEditing.Empty();

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => Mode != CameraMode.Off;

    /// <summary>Enters editing; from live, takes the Director offline.</summary>
    public EditOutcome Edit()
    {
        switch (Mode)
        {
            case CameraMode.Editing:
                return EditOutcome.Unchanged;
            case CameraMode.Live:
                Director.GoOffline();
                Mode = CameraMode.Editing;
                return EditOutcome.FromLive;
            default:
                Mode = CameraMode.Editing;
                return EditOutcome.FromOff;
        }
    }

    /// <summary>Resumes a paused shot, leaves a playing one alone, otherwise restarts.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused) return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return Restart();
    }

    /// <summary>Goes live with the track from its start. Refused with no points.</summary>
    public PlayOutcome Restart()
    {
        if (Track.Points.Count == 0) return PlayOutcome.Refused;

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
    }

    /// <summary>Holds the current frame and stays live. Returns false unless live.</summary>
    public bool Stop()
    {
        if (Mode != CameraMode.Live) return false;
        Director.Pause();
        return true;
    }

    /// <summary>Turns off and takes the Director offline. Returns false if already off.</summary>
    public bool Release()
    {
        if (Mode == CameraMode.Off) return false;
        Director.GoOffline();
        Mode = CameraMode.Off;
        return true;
    }

    /// <summary>Applies <paramref name="change"/> if editing and the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";

        try
        {
            var result = change(Track);
            _ = new TrackEvaluator(result);
            Track = result;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
