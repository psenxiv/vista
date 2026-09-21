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

    /// <summary>The track Edit builds and Play plays. Changed only through the edit methods and undo.</summary>
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
                freeCam.Enable(start.Value.Position, 0f, start.Value.Fov);
                movement.Hold();
                TakeCamera();
                break;
            case EditOutcome.FromLive:
                FlyFrom(start.Value);
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

    /// <summary>Goes live paused at the track's start, leaving the UI shown. Refused with no points.</summary>
    public void Cue() => Apply(state.Cue());

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

    /// <summary>The selected point's index, or null.</summary>
    public int? Selected => state.Selected;

    /// <summary>Selects a point while editing; null or out of range clears the selection.</summary>
    public void Select(int? index) => state.Select(index);

    /// <summary>The track's length in seconds.</summary>
    public double Duration => state.Duration;

    /// <summary>The track's frame at <paramref name="time"/> seconds, or null with no points.</summary>
    public CameraState? FrameAt(double time) => state.FrameAt(time);

    /// <summary>True while the scrub head is being dragged.</summary>
    public bool Scrubbing => state.Scrubbing;

    /// <summary>Seconds under the scrub head.</summary>
    public double ScrubHead => state.ScrubHead;

    /// <summary>Starts dragging the scrub head; while editing the camera shows the scrubbed frame.</summary>
    public void BeginScrub() => state.BeginScrub();

    /// <summary>Moves the scrub head; live, playback seeks there.</summary>
    public void ScrubTo(double time) => state.ScrubTo(time);

    /// <summary>Stops dragging the scrub head; while editing the free-cam flies on from the frame shown.</summary>
    public void EndScrub()
    {
        var fromEditing = state.Mode == CameraMode.Editing && state.Scrubbing;
        state.EndScrub();
        if (fromEditing && state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }

    /// <summary>Puts the free-cam at point <paramref name="index"/> while editing, as a scrub release would.</summary>
    public void JumpToPoint(int index)
    {
        if (state.Mode != CameraMode.Editing || index < 0 || index >= state.Track.Points.Count) return;
        state.ScrubTo(TrackEditing.PointSeconds(state.Track, index));
        if (state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => state.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => state.CanRedo;

    /// <summary>Restores the track and selection before the last change.</summary>
    public bool Undo() => state.Undo();

    /// <summary>Re-applies the last undone change.</summary>
    public bool Redo() => state.Redo();

    /// <summary>Adds the current camera to the end of the track. Returns why it was refused, or null.</summary>
    public string? AddToEnd() => WithCurrentPoint(state.AddToEnd);

    /// <summary>Adds the current camera after the selected point and selects it. Returns why it was refused, or null.</summary>
    public string? AddAfterSelected() => WithCurrentPoint(state.AddAfterSelected);

    /// <summary>Replaces the selected point with the current camera. Returns why it was refused, or null.</summary>
    public string? OverwriteSelected() => WithCurrentPoint(state.OverwriteSelected);

    /// <summary>Replaces point <paramref name="index"/>, keeping its timing. Returns why it was refused, or null.</summary>
    public string? ReplacePoint(int index, ControlPoint point) => state.ReplacePoint(index, point);

    /// <summary>Deletes the selected point. Returns why it was refused, or null.</summary>
    public string? DeleteSelected() => state.DeleteSelected();

    /// <summary>Deletes point <paramref name="index"/>, keeping any other selection. Returns why it was refused, or null.</summary>
    public string? DeletePoint(int index) => state.DeletePoint(index);

    /// <summary>Moves a point in the order. Returns why it was refused, or null.</summary>
    public string? MovePoint(int from, int to) => state.MovePoint(from, to);

    /// <summary>Starts a live point edit that previews on the track and ends as one undo step.</summary>
    public void BeginPointEdit() => state.BeginPointEdit();

    /// <summary>Replaces point <paramref name="index"/> during a live edit. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point) => state.PreviewPoint(index, point);

    /// <summary>Ends a live point edit as one undo step if anything changed.</summary>
    public void EndPointEdit() => state.EndPointEdit();

    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be added while editing.";
        if (state.Scrubbing) return "Points cannot be added while scrubbing.";

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return "Cannot read the camera.";

        var s = camera.Value;
        var (yaw, pitch) = angles.Value;
        return edit(new ControlPoint(s.Position, yaw, pitch, s.Fov, freeCam.Roll));
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!ownership.IsOwned) return null;

        var frame = state.Mode switch
        {
            CameraMode.Editing => state.Scrubbing && state.FrameAt(state.ScrubHead) is { } scrubbed ? scrubbed : freeCam.Tick(dt),
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
            case PlayOutcome.StartedFromOff or PlayOutcome.CuedFromOff:
                movement.Hold();
                TakeCamera();
                break;
        }

        freeCam.Disable();
        if (outcome is PlayOutcome.Cued or PlayOutcome.CuedFromOff)
        {
            Plugin.Log.Information("[ccam] mode: live, cued, {Count} points", state.Track.Points.Count);
            return;
        }

        GameUi.Hide();
        Plugin.Log.Information("[ccam] mode: live, {Count} points", state.Track.Points.Count);
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        ownership.Take();
    }

    /// <summary>Puts the free-cam at <paramref name="frame"/>, keeping its aim by writing the game's yaw and pitch within its limits.</summary>
    private void FlyFrom(CameraState frame)
    {
        freeCam.Enable(frame.Position, frame.Roll, frame.Fov);
        var (yaw, pitch) = TrackAim.FromDirection(frame.LookAt - frame.Position);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        CameraAccess.WriteAngles(yaw, Math.Clamp(pitch, min, max));
    }
}
