using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Plugin.Game;

namespace Vista.Plugin.Session;

/// <summary>Carries out the session's mode changes in game: free-cam, movement lock, camera ownership and UI; and edits that need the camera.</summary>
internal sealed class GameSession
{
    private readonly NearbyCharacters characters = new();
    private readonly SessionState state;
    private readonly FreeCam freeCam = new();
    private readonly Configuration config;
    private readonly MovementLock movement;
    private bool owned;
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;
    private bool previewedLastFrame;

    public GameSession(Configuration config, MovementLock movement)
    {
        this.config = config;
        this.movement = movement;
        state = new SessionState(Ground.Below, characters);
    }

    /// <summary>The session this carries out.</summary>
    public SessionState State => state;

    /// <summary>Whether Live hides the game UI while it plays; toggling it while Live plays applies at once.</summary>
    public bool HideUiInLive
    {
        get => config.HideUiInLive;
        set
        {
            if (config.HideUiInLive == value)
                return;
            config.HideUiInLive = value;
            config.Save();
            if (state.Mode != CameraMode.Live)
                return;
            if (value && !state.Director.IsPaused && !state.Director.IsFinished)
                GameUi.Hide();
            else if (!value)
                GameUi.Restore();
        }
    }

    /// <summary>The characters loaded nearby, as last read.</summary>
    public NearbyCharacters Characters => characters;

    /// <summary>Reads the characters loaded nearby. Call once a frame from Framework.Update.</summary>
    public void RefreshCharacters() => characters.Update(CharacterTable.Read());

    /// <summary>True while the plugin writes the camera.</summary>
    public bool OwnsCamera => owned;

    /// <summary>The free-cam's speed setting.</summary>
    public FlySpeed Speed => freeCam.Speed;

    /// <summary>The free camera's position while editing.</summary>
    public Vector3 CameraPosition
    {
        get => freeCam.Position;
        set => freeCam.Position = value;
    }

    /// <summary>The free camera's roll in radians; setting it rolls about the facing, which stays put.</summary>
    public float CameraRoll
    {
        get => CameraRotation.ToAngles(freeCam.Rotation).Roll;
        set => freeCam.Rotation = FreeCamMotion.Roll(freeCam.Rotation, value - CameraRoll);
    }

    /// <summary>The field of view the camera is looking through, in radians.</summary>
    public float CameraFov
    {
        get => freeCam.Fov;
        set => freeCam.Fov = value;
    }

    /// <summary>The game's field of view from just before Vista took the camera, or null when Vista does not hold it.</summary>
    public float? TakeoverFov => snapshotBeforeTakeover?.Fov;

    /// <summary>The camera's yaw and pitch: the free cam's while it flies, otherwise the game's, or null when the game's cannot be read.</summary>
    public (float Yaw, float Pitch)? CameraAngles
    {
        get
        {
            if (!freeCam.Enabled)
                return CameraAccess.ReadAngles();
            var (yaw, pitch, _) = CameraRotation.ToAngles(freeCam.Rotation);
            return (yaw, pitch);
        }
    }

    /// <summary>Turns the free camera to face <paramref name="yaw"/> and <paramref name="pitch"/>, keeping its roll.</summary>
    public void TurnCamera(float yaw, float pitch) =>
        freeCam.Rotation = CameraRotation.FromAngles(yaw, pitch, CameraRoll);

    /// <summary>Turns the free camera's picture upright about its facing.</summary>
    public void LevelCameraRoll()
    {
        var forward = CameraRotation.Forward(freeCam.Rotation);
        freeCam.Rotation = CameraRotation.FromBasis(forward, CameraRotation.Upright(forward));
    }

    /// <summary>Moves the free camera by (forward, up, right) along its own axes, as the fly keys do.</summary>
    public void NudgeCamera(Vector3 input) =>
        freeCam.Position = FreeCamMotion.Step(freeCam.Position, input, freeCam.Rotation, 1f, 1f);

    /// <summary>Starts free-cam: from Off or View at the game camera, from Live at the current frame. No-op while editing.</summary>
    public void EnterEdit()
    {
        if (state.Mode == CameraMode.Editing)
            return;

        var start = state.Mode == CameraMode.Live ? lastFrame ?? CameraAccess.ReadState() : CameraAccess.ReadState();
        if (start is null)
        {
            Plugin.Log.Error("[vista] cannot read camera state.");
            return;
        }

        switch (state.Edit())
        {
            case EditOutcome.FromGame:
                previewedLastFrame = false;
                var (yaw, pitch) = CameraAccess.ReadAngles() ?? (0f, 0f);
                freeCam.Enable(start.Value.Position, CameraRotation.FromAngles(yaw, pitch, 0f), start.Value.Fov);
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
        Plugin.Log.Information("[vista] mode: editing");
    }

    /// <summary>In Edit, previews from the scrub head; live, resumes a paused shot or leaves a playing one alone; otherwise goes live with the playlist. Refused when nothing can play.</summary>
    public void StartPlay()
    {
        var previewing = state.Mode == CameraMode.Editing;
        Apply(state.Play(), previewing);
    }

    /// <summary>In Edit, previews from the beginning; otherwise goes live with the playlist from the start, taking the camera if in Off or View. Refused when nothing can play.</summary>
    public void RestartPlay()
    {
        var previewing = state.Mode == CameraMode.Editing;
        Apply(state.Restart(), previewing);
    }

    /// <summary>Goes live with the playlist paused at its start, leaving the UI shown. Refused when nothing can play.</summary>
    public void CueLive() => Apply(state.Cue());

    /// <summary>Live, holds the current frame; in Edit, stops a preview.</summary>
    public void StopPlay()
    {
        var editing = state.Mode == CameraMode.Editing;
        if (state.Stop())
            Plugin.Log.Information(editing ? "[vista] preview stopped" : "[vista] paused");
    }

    /// <summary>Goes to Off, or to View when asked: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason, CameraMode to = CameraMode.Off)
    {
        if (!state.Release(to) && !owned)
            return;

        freeCam.Disable();
        GameUi.Restore();
        movement.Release();
        lastFrame = null;
        previewedLastFrame = false;
        owned = false;

        // Without this the game carries on from our values rather than its own,
        // which leaves the camera wrong long after we stop writing.
        if (snapshotBeforeTakeover is { } snapshot)
        {
            CameraAccess.Restore(snapshot);
            snapshotBeforeTakeover = null;
        }

        Plugin.Log.Information("[vista] camera released: {Reason}", reason);
    }

    /// <summary>Adds a preset as a new track on the ground under the camera and edits it. Returns why it was refused, or null.</summary>
    public string? AddPreset(Preset preset) => state.AddPreset(preset, CameraPosition);

    /// <summary>Adds the current camera to the end of the track. Returns why it was refused, or null.</summary>
    public string? AddToEnd() => WithCurrentPoint(state.AddToEnd);

    /// <summary>Adds the current camera after the selected point and selects it. Returns why it was refused, or null.</summary>
    public string? AddAfterSelected() => WithCurrentPoint(state.AddAfterSelected);

    /// <summary>Replaces the selected point with the current camera. Returns why it was refused, or null.</summary>
    public string? OverwriteSelected() => WithCurrentPoint(state.OverwriteSelected);

    /// <summary>Sets the aim mode; the first Look At with no points goes ahead of the camera. Returns why it was refused, or null.</summary>
    public string? SetAim(AimMode aim)
    {
        if (CameraPoint() is { } camera)
            return state.SetAim(aim, camera);
        var placesFromCamera = aim == AimMode.LookAt && state.Track is { Points.Count: 0, LookAtPlaced: false };
        return state.Mode == CameraMode.Editing && placesFromCamera
            ? "Cannot read the camera."
            : state.SetAim(aim, new ControlPoint(Vector3.Zero, 0f, 0f, 1f));
    }

    /// <summary>Edits a track and flies the editor camera to its first point, as a point's double-click does. Returns why it was refused, or null.</summary>
    public string? FlyToFirstPoint(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is null)
            JumpToPoint(0);
        return refusal;
    }

    /// <summary>Stops dragging the scrub head; while editing the free-cam flies on from the frame shown.</summary>
    public void FinishScrub()
    {
        var fromEditing = state.Mode == CameraMode.Editing && state.Transport.Scrubbing;
        state.Transport.EndScrub();
        if (fromEditing && state.World.FrameAt(state.Transport.ScrubHead) is { } frame)
            FlyFrom(frame);
    }

    /// <summary>Puts the free-cam at point <paramref name="index"/> while editing, as a scrub release would.</summary>
    public void JumpToPoint(int index)
    {
        if (state.Mode != CameraMode.Editing || index < 0 || index >= state.Track.Points.Count)
            return;
        state.Transport.ScrubTo(state.World.Evaluator.PointSeconds(index));
        if (state.World.FrameAt(state.Transport.ScrubHead) is { } frame)
            FlyFrom(frame);
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!owned)
            return null;

        var frame = state.Mode switch
        {
            CameraMode.Editing => EditingFrame(dt),
            CameraMode.Live => state.Director.Tick(dt),
            _ => null,
        };

        lastFrame = frame;
        return frame;
    }

    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point, or the previewed frame while previewing.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing)
            return "Points can only be added while editing.";
        if (state.Transport.Scrubbing)
            return "Points cannot be added while scrubbing.";
        return CameraPoint() is { } point ? edit(point) : "Cannot read the camera.";
    }

    /// <summary>The current camera as a control point, or the previewed frame while previewing; null when the camera can't be read.</summary>
    private ControlPoint? CameraPoint()
    {
        if (state.Transport.Previewing && lastFrame is { } previewed)
        {
            var (previewYaw, previewPitch, previewRoll) = CameraRotation.ToAngles(
                CameraRotation.FromBasis(previewed.LookAt - previewed.Position, previewed.Up)
            );
            return new ControlPoint(previewed.Position, previewYaw, previewPitch, previewed.Fov, previewRoll);
        }

        var camera = CameraAccess.ReadState();
        if (camera is null)
            return null;
        if (freeCam.Enabled)
        {
            var (yaw, pitch, roll) = CameraRotation.ToAngles(freeCam.Rotation);
            return new ControlPoint(camera.Value.Position, yaw, pitch, camera.Value.Fov, roll);
        }

        return CameraAccess.ReadAngles() is { } angles
            ? new ControlPoint(camera.Value.Position, angles.Yaw, angles.Pitch, camera.Value.Fov)
            : null;
    }

    /// <summary>While editing: the preview's frame, the scrubbed frame, or the free-cam, handing the free-cam the last frame when a preview stops.</summary>
    private CameraState? EditingFrame(float dt)
    {
        var transport = state.Transport;
        if (transport.Previewing && FreeCam.HasFlightInput())
            transport.StopPreview();
        var frame = transport.AdvancePreview(dt);

        if (previewedLastFrame && !transport.Previewing)
        {
            previewedLastFrame = false;
            if ((frame ?? lastFrame ?? state.World.FrameAt(transport.ScrubHead)) is { } last)
                FlyFrom(last);
            return freeCam.Tick(dt);
        }

        previewedLastFrame = transport.Previewing;
        if (frame is { } previewing)
            return previewing;
        return transport.Scrubbing && state.World.FrameAt(transport.ScrubHead) is { } scrubbed
            ? scrubbed
            : freeCam.Tick(dt);
    }

    /// <summary>Carries out a play or restart outcome in game. <paramref name="previewRefusal"/> says a refusal is the edited track's, not the playlist's.</summary>
    private void Apply(PlayOutcome outcome, bool previewRefusal = false)
    {
        switch (outcome)
        {
            case PlayOutcome.Previewed:
                Plugin.Log.Information("[vista] preview");
                return;
            case PlayOutcome.Refused:
                Plugin.Log.Debug(
                    previewRefusal
                        ? "[vista] cannot preview a track with no points."
                        : "[vista] nothing to play: add a track with points to the playlist."
                );
                return;
            case PlayOutcome.ReHid:
                if (HideUiInLive)
                    GameUi.Hide();
                return;
            case PlayOutcome.Resumed:
                if (HideUiInLive)
                    GameUi.Hide();
                Plugin.Log.Information("[vista] resumed");
                return;
            case PlayOutcome.StartedFromGame or PlayOutcome.CuedFromGame:
                movement.Hold();
                TakeCamera();
                break;
        }

        freeCam.Disable();
        if (outcome is PlayOutcome.Cued or PlayOutcome.CuedFromGame)
        {
            Plugin.Log.Information("[vista] mode: live, cued, {Count} playlist entries", state.Scene.Playlist.Count);
            return;
        }

        if (HideUiInLive)
            GameUi.Hide();
        Plugin.Log.Information("[vista] mode: live, {Count} playlist entries", state.Scene.Playlist.Count);
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        owned = true;
    }

    /// <summary>Puts the free-cam at <paramref name="frame"/>, facing its way with its up.</summary>
    private void FlyFrom(CameraState frame)
    {
        previewedLastFrame = false;
        freeCam.Enable(frame.Position, CameraRotation.FromBasis(frame.LookAt - frame.Position, frame.Up), frame.Fov);
    }
}
