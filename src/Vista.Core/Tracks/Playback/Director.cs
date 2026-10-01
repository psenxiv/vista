using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;

namespace Vista.Core.Tracks.Playback;

/// <summary>Holds live mode and the shot on program; each tick says where the camera goes, or null to leave it be.</summary>
public sealed class Director : IPlayingShot
{
    private PlaylistPlayback? _playback;
    private bool resumeAfterScrub;
    private readonly NearbyCharacters? targets;

    /// <summary>A Director whose playbacks find watched or followed characters with <paramref name="targets"/>.</summary>
    public Director(NearbyCharacters? targets = null) => this.targets = targets;

    /// <summary>True once <see cref="GoLive"/> has been called and <see cref="GoOffline"/> has not.</summary>
    public bool IsLive => _playback is not null;

    /// <summary>True while live and paused; frames stop advancing.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True once the current shot's playback has finished; false otherwise.</summary>
    public bool IsFinished => _playback?.IsFinished ?? false;

    /// <summary>True while the current shot runs: live, neither paused nor finished.</summary>
    public bool IsPlaying => IsLive && !IsPaused && !IsFinished;

    /// <summary>Where the current shot's camera is in the playing entry; 0 before going live.</summary>
    public double ShotTime => _playback?.ShotTime ?? 0.0;

    /// <summary>The playlist being played, or null before going live.</summary>
    public PlaylistPlayback? Playlist => _playback;

    /// <summary>The current shot laid end to end, or null before going live.</summary>
    public PlaylistTimeline? Timeline => _playback?.Timeline;

    /// <summary>Seconds through the current shot; 0 before going live.</summary>
    public double Head => _playback?.PlaylistTime ?? 0.0;

    /// <summary>The playing entry's index among the current shot's segments; 0 before going live.</summary>
    public int EntryIndex => _playback?.Index ?? 0;

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Puts <paramref name="shot"/> on program: live on, unpaused, restarted from zero, any scrub ended. Unchanged if a track in it can't play.</summary>
    public void GoLive(PlaylistShot shot)
    {
        _playback = new PlaylistPlayback(shot.Items, shot.Loops, targets);
        IsPaused = false;
        Scrubbing = false;
    }

    /// <summary>Plays the live shot: on from a pause, or from its start once it has finished. No effect otherwise.</summary>
    public void Play()
    {
        if (IsFinished)
            Restart();
        else if (IsLive)
            Resume();
    }

    /// <summary>Holds the current frame. No effect unless live.</summary>
    public void Pause()
    {
        if (IsLive)
            IsPaused = true;
    }

    /// <summary>Continues from the paused frame.</summary>
    public void Resume() => IsPaused = false;

    /// <summary>Takes live mode off, clears pause and ends any scrub. <see cref="Tick"/> returns null until the next <see cref="GoLive"/>.</summary>
    public void GoOffline()
    {
        _playback = null;
        IsPaused = false;
        Scrubbing = false;
    }

    /// <summary>Starts dragging the head: holds the live shot until <see cref="EndScrub"/>. No effect unless live, or while already scrubbing.</summary>
    public void BeginScrub()
    {
        if (!IsLive || Scrubbing)
            return;
        Scrubbing = true;
        resumeAfterScrub = !IsPaused;
        Pause();
    }

    /// <summary>Jumps the live shot to <paramref name="time"/> through it, clamped to it, keeping pause. No effect otherwise.</summary>
    public void ScrubTo(double time) => _playback?.SeekPlaylist(time);

    /// <summary>Stops dragging the head; a shot that was unpaused as the scrub began carries on.</summary>
    public void EndScrub()
    {
        if (!Scrubbing)
            return;
        Scrubbing = false;
        if (resumeAfterScrub)
            Resume();
    }

    /// <summary>Plays the live playlist again from its start, unpaused. No effect otherwise.</summary>
    public void Restart()
    {
        if (_playback is null)
            return;
        _playback.Restart();
        IsPaused = false;
    }

    /// <summary>Where the camera should be this frame, or null to leave the game camera alone.</summary>
    public CameraState? Tick(float dt) => IsLive ? _playback!.Advance(IsPaused ? 0f : dt) : null;
}
