using CinematicCam.Core;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin;

/// <summary>The plugin's mode, the track being built, and the camera source that follows the mode.</summary>
internal sealed class CameraSession
{
    private readonly FreeCam freeCam = new();
    private readonly Director director = new();
    private readonly MovementLock movement;
    private readonly CameraOwnership ownership = new();
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;

    public CameraSession(MovementLock movement) => this.movement = movement;

    public CameraMode Mode { get; private set; }

    /// <summary>The track Edit builds and Play plays. Changed only through <see cref="ChangeTrack"/>.</summary>
    public Track Track { get; private set; } = TrackEditing.Empty();

    /// <summary>Read-only view of playback state. Check IsLive before IsPaused or IsFinished.</summary>
    public Director Director => director;

    /// <summary>True while the plugin writes the camera.</summary>
    public bool OwnsCamera => ownership.IsOwned;

    /// <summary>The <c>/ccam hold</c> debug state, used only while off.</summary>
    public CameraState? TestState { get; set; }

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => Mode != CameraMode.Off;

    /// <summary>Starts free-cam: from Off at the game camera, from Live at the current frame. No-op while editing.</summary>
    public void Edit()
    {
        switch (Mode)
        {
            case CameraMode.Editing:
                return;
            case CameraMode.Off:
            {
                var start = CameraAccess.ReadState();
                if (start is null) { Plugin.Log.Error("[ccam] cannot read camera state."); return; }
                freeCam.Enable(start.Value.Position);
                movement.Hold();
                TakeCamera();
                break;
            }
            case CameraMode.Live:
            {
                var start = lastFrame ?? CameraAccess.ReadState();
                if (start is null) { Plugin.Log.Error("[ccam] cannot read camera state."); return; }
                director.GoOffline();
                freeCam.Enable(start.Value.Position);
                break;
            }
        }

        Mode = CameraMode.Editing;
        Plugin.Log.Information("[ccam] mode: editing");
    }

    /// <summary>Goes live with the current track from its start, taking the camera if off. Refused with no points.</summary>
    public void Play()
    {
        if (Track.Points.Count == 0) { Plugin.Log.Error("[ccam] cannot play a track with no points."); return; }

        director.GoLive(new TrackShot(Track));

        if (Mode == CameraMode.Off)
        {
            movement.Hold();
            TakeCamera();
        }

        freeCam.Disable();
        Mode = CameraMode.Live;
        Plugin.Log.Information("[ccam] mode: live, {Count} points", Track.Points.Count);
    }

    /// <summary>Holds the current frame and stays live. No effect unless live.</summary>
    public void Stop()
    {
        if (Mode != CameraMode.Live) return;
        director.Pause();
        Plugin.Log.Information("[ccam] paused");
    }

    /// <summary>Turns the plugin off: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason)
    {
        if (Mode == CameraMode.Off && !ownership.IsOwned && TestState is null) return;

        director.GoOffline();
        freeCam.Disable();
        Mode = CameraMode.Off;
        movement.Release();
        TestState = null;
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

    /// <summary>Holds the camera at a fixed debug state while off.</summary>
    public void Hold(CameraState state)
    {
        TestState = state;
        TakeCamera();
    }

    /// <summary>Applies <paramref name="change"/> to the track if the result can be played. Returns why it was refused, or null once applied.</summary>
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

    /// <summary>Appends the current camera as a control point. Returns why it was refused, or null once appended.</summary>
    public string? CapturePoint()
    {
        if (Mode != CameraMode.Editing) return "Points can only be captured while editing.";

        var state = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (state is null || angles is null) return "Cannot read the camera.";

        var s = state.Value;
        var (yaw, pitch) = angles.Value;
        return ChangeTrack(track => TrackEditing.Append(track, new ControlPoint(s.Position, yaw, pitch, s.Fov)));
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!ownership.IsOwned) return null;

        var state = Mode switch
        {
            CameraMode.Editing => freeCam.Tick(dt),
            CameraMode.Live => director.Tick(dt),
            _ => TestState,
        };

        lastFrame = state;
        return state;
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        ownership.Take();
    }
}
