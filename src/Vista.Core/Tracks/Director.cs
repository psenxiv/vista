using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Holds live mode and the shot on program; each tick says where the camera goes, or null to leave it be.</summary>
public sealed class Director
{
    private Shot? _shot;
    private IPlayback? _playback;
    private readonly IAimTargets? targets;

    /// <summary>A Director whose playbacks find followed characters with <paramref name="targets"/>.</summary>
    public Director(IAimTargets? targets = null) => this.targets = targets;

    /// <summary>True once <see cref="GoLive"/> has been called and <see cref="GoOffline"/> has not.</summary>
    public bool IsLive { get; private set; }

    /// <summary>True while live and paused; frames stop advancing.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True once the current track or playlist's playback has finished; false otherwise.</summary>
    public bool IsFinished => _playback?.IsFinished ?? false;

    /// <summary>Where the current track or playlist's camera is in the shot; 0 for other shots or before going live.</summary>
    public double ShotTime => _playback?.ShotTime ?? 0.0;

    /// <summary>The current shot's length in seconds; 0 for the game camera or before going live.</summary>
    public double ShotLength => _playback?.ShotLength ?? 0.0;

    /// <summary>The playlist being played, or null for other shots.</summary>
    public PlaylistPlayback? Playlist => _playback as PlaylistPlayback;

    /// <summary>Puts <paramref name="shot"/> on program: live on, unpaused, restarted from zero. Unchanged if the track throws.</summary>
    public void GoLive(Shot shot)
    {
        var playback = shot switch
        {
            TrackShot t => (IPlayback)new TrackPlayback(t.Track, targets),
            PlaylistShot p => new PlaylistPlayback(p.Items, p.Loops, targets),
            _ => null,
        };
        _shot = shot;
        _playback = playback;
        IsLive = true;
        IsPaused = false;
    }

    /// <summary>Holds the current frame. No effect unless live.</summary>
    public void Pause()
    {
        if (IsLive) IsPaused = true;
    }

    /// <summary>Continues from the paused frame. No effect unless live.</summary>
    public void Resume()
    {
        if (IsLive) IsPaused = false;
    }

    /// <summary>Takes live mode off and clears pause. <see cref="Tick"/> returns null until the next <see cref="GoLive"/>.</summary>
    public void GoOffline()
    {
        IsLive = false;
        IsPaused = false;
    }

    /// <summary>Jumps the live track or playlist to <paramref name="time"/>, keeping pause. No effect otherwise.</summary>
    public void Seek(double time)
    {
        if (IsLive) _playback?.Seek(time);
    }

    /// <summary>Where the camera should be this frame, or null to leave the game camera alone.</summary>
    public CameraState? Tick(float dt)
    {
        if (!IsLive) return null;

        return _shot switch
        {
            TrackShot or PlaylistShot => _playback!.Advance(IsPaused ? 0f : dt),
            _ => null,
        };
    }
}
