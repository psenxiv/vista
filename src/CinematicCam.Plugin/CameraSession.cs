using CinematicCam.Core;
using CinematicCam.Plugin.Game;

namespace CinematicCam.Plugin;

/// <summary>The plugin's mode, the track being built, and the camera source that follows the mode.</summary>
internal sealed class CameraSession
{
    private readonly FreeCam freeCam = new();
    private readonly Director director = new();
    private readonly MovementLock movement;
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;

    public CameraSession(MovementLock movement) => this.movement = movement;

    public CameraMode Mode { get; private set; }

    /// <summary>The track Edit builds and Play plays. Only change it while editing.</summary>
    public Track Track { get; set; } = TrackEditing.Empty();

    /// <summary>Read-only view of playback state. Check IsLive before IsPaused or IsFinished.</summary>
    public Director Director => director;

    public CameraOwnership Ownership { get; } = new();

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

    /// <summary>Goes live with the current track from its start, taking the camera if off.</summary>
    public void Play()
    {
        if (Mode == CameraMode.Off)
        {
            movement.Hold();
            TakeCamera();
        }

        freeCam.Disable();
        director.GoLive(new TrackShot(Track));
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
        if (Mode == CameraMode.Off && !Ownership.IsOwned && TestState is null) return;

        director.GoOffline();
        freeCam.Disable();
        Mode = CameraMode.Off;
        movement.Release();
        TestState = null;
        lastFrame = null;
        Ownership.Release(reason);

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

    /// <summary>A control point from the current camera, or null if the camera cannot be read.</summary>
    public static ControlPoint? CapturePoint()
    {
        var state = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (state is null || angles is null) return null;

        var s = state.Value;
        var (yaw, pitch) = angles.Value;
        return new ControlPoint(s.Position, yaw, pitch, s.Fov);
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!Ownership.IsOwned) return null;

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
        Ownership.Take();
    }
}
