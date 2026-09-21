using CinematicCam.Core.Camera;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin.Session;

/// <summary>Carries out the session's mode changes in game: free-cam, movement lock, camera ownership and UI.</summary>
internal sealed class CameraSession
{
    private readonly SessionState state = new();
    private readonly FreeCam freeCam = new();
    private readonly MovementLock movement;
    private readonly CameraOwnership ownership = new();
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;

    public CameraSession(MovementLock movement) => this.movement = movement;

    public CameraMode Mode => state.Mode;

    /// <summary>The track Edit builds and Play plays. Changed only through <see cref="ChangeTrack"/>.</summary>
    public Track Track => state.Track;

    /// <summary>Read-only view of playback state. Check IsLive before IsPaused or IsFinished.</summary>
    public Director Director => state.Director;

    /// <summary>True while the plugin writes the camera.</summary>
    public bool OwnsCamera => ownership.IsOwned;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => state.LocksInput;

    /// <summary>The free-cam's speed setting.</summary>
    public FlySpeed Speed => freeCam.Speed;

    /// <summary>Starts free-cam: from Off at the game camera, from Live at the current frame. No-op while editing.</summary>
    public void Edit()
    {
        if (state.Mode == CameraMode.Editing) return;

        var start = state.Mode == CameraMode.Live ? lastFrame ?? CameraAccess.ReadState() : CameraAccess.ReadState();
        if (start is null) { Plugin.Log.Error("[ccam] cannot read camera state."); return; }

        switch (state.Edit())
        {
            case EditOutcome.FromOff:
                freeCam.Enable(start.Value.Position);
                movement.Hold();
                TakeCamera();
                break;
            case EditOutcome.FromLive:
                freeCam.Enable(start.Value.Position, start.Value.Roll);
                break;
            default:
                return;
        }

        GameUi.Restore();
        Plugin.Log.Information("[ccam] mode: editing");
    }

    /// <summary>Resumes a paused shot, re-hides the UI of a playing one, otherwise starts from the top.</summary>
    public void Play() => Apply(state.Play());

    /// <summary>Goes live with the current track from its start, taking the camera if off. Refused with no points.</summary>
    public void Restart() => Apply(state.Restart());

    /// <summary>Holds the current frame and stays live. No effect unless live.</summary>
    public void Stop()
    {
        if (state.Stop()) Plugin.Log.Information("[ccam] paused");
    }

    /// <summary>Turns the plugin off: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason)
    {
        if (!state.Release() && !ownership.IsOwned) return;

        freeCam.Disable();
        GameUi.Restore();
        movement.Release();
        lastFrame = null;
        ownership.Release(reason);

        // Without this the game carries on from our values rather than its own,
        // which leaves the camera wrong long after we stop writing.
        if (snapshotBeforeTakeover is { } snapshot)
        {
            CameraAccess.Restore(snapshot);
            snapshotBeforeTakeover = null;
        }

        Plugin.Log.Information("[ccam] camera released: {Reason}", reason);
    }

    /// <summary>Applies <paramref name="change"/> to the track if the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change) => state.ChangeTrack(change);

    /// <summary>Appends the current camera as a control point. Returns why it was refused, or null once appended.</summary>
    public string? CapturePoint()
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be captured while editing.";

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return "Cannot read the camera.";

        var s = camera.Value;
        var (yaw, pitch) = angles.Value;
        return state.ChangeTrack(track => TrackEditing.Append(track, new ControlPoint(s.Position, yaw, pitch, s.Fov, freeCam.Roll)));
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!ownership.IsOwned) return null;

        var frame = state.Mode switch
        {
            CameraMode.Editing => freeCam.Tick(dt),
            CameraMode.Live => state.Director.Tick(dt),
            _ => null,
        };

        lastFrame = frame;
        return frame;
    }

    /// <summary>Carries out a play or restart outcome in game.</summary>
    private void Apply(PlayOutcome outcome)
    {
        switch (outcome)
        {
            case PlayOutcome.Refused:
                Plugin.Log.Error("[ccam] cannot play a track with no points.");
                return;
            case PlayOutcome.ReHid:
                GameUi.Hide();
                return;
            case PlayOutcome.Resumed:
                GameUi.Hide();
                Plugin.Log.Information("[ccam] resumed");
                return;
            case PlayOutcome.StartedFromOff:
                movement.Hold();
                TakeCamera();
                break;
        }

        freeCam.Disable();
        GameUi.Hide();
        Plugin.Log.Information("[ccam] mode: live, {Count} points", state.Track.Points.Count);
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        ownership.Take();
    }
}
