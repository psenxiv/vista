using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;

namespace Vista.Plugin.Session;

/// <summary>Carries out the session's mode changes in game: free-cam, movement lock, camera ownership and UI.</summary>
internal sealed class CameraSession
{
    private readonly NearbyCharacters characters = new();
    private readonly SessionState state;
    private readonly FreeCam freeCam = new();
    private readonly MovementLock movement;
    private bool owned;
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;
    private bool previewedLastFrame;
    private bool hideUiInLive;

    public CameraSession(MovementLock movement)
    {
        this.movement = movement;
        state = new SessionState(Ground.Below, characters);
    }

    public CameraMode Mode => state.Mode;

    /// <summary>Whether Live hides the game UI while it plays; toggling it while Live plays applies at once.</summary>
    public bool HideUiInLive
    {
        get => hideUiInLive;
        set
        {
            if (hideUiInLive == value) return;
            hideUiInLive = value;
            if (state.Mode != CameraMode.Live) return;
            if (value && !state.Director.IsPaused && !state.Director.IsFinished) GameUi.Hide();
            else if (!value) GameUi.Restore();
        }
    }

    /// <summary>The edited track in the world as the editor shows it.</summary>
    public Track Track => state.Track;

    /// <summary>The edited track as stored; a new instance only when it is edited.</summary>
    public Track StoredTrack => state.StoredTrack;

    /// <summary>The tracks being edited, their order and which are hidden.</summary>
    public Scene Scene => state.Scene;

    /// <summary>The Id of the track the editor works on.</summary>
    public Guid EditedTrackId => state.EditedTrackId;

    /// <summary>Adds an empty track and edits it. Returns why it was refused, or null.</summary>
    public string? AddTrack() => state.AddTrack();

    /// <summary>Renames a track. Returns why it was refused, or null.</summary>
    public string? RenameTrack(Guid id, string name) => state.RenameTrack(id, name);

    /// <summary>Copies a track after itself and edits the copy. Returns why it was refused, or null.</summary>
    public string? DuplicateTrack(Guid id) => state.DuplicateTrack(id);

    /// <summary>Deletes tracks. Returns why it was refused, or null.</summary>
    public string? DeleteTracks(IReadOnlyCollection<Guid> ids) => state.DeleteTracks(ids);

    /// <summary>Moves tracks as a block onto <paramref name="target"/>, or the end when null. Returns why it was refused, or null.</summary>
    public string? MoveTracks(IReadOnlyCollection<Guid> ids, Guid grabbed, Guid? target) => state.MoveTracks(ids, grabbed, target);

    /// <summary>Hides or shows tracks; hiding skips the edited one. Returns why it was refused, or null.</summary>
    public string? SetTracksHidden(IReadOnlyCollection<Guid> ids, bool hidden) => state.SetTracksHidden(ids, hidden);

    /// <summary>Adds a playlist entry for each track, in Hierarchy order. Returns why it was refused, or null.</summary>
    public string? AddToPlaylist(IReadOnlyCollection<Guid> ids, int? index = null) => state.AddToPlaylist(ids, index);

    /// <summary>Removes playlist entries. Returns why it was refused, or null.</summary>
    public string? RemoveFromPlaylist(IReadOnlyCollection<Guid> ids) => state.RemoveFromPlaylist(ids);

    /// <summary>Moves playlist entries as a block onto <paramref name="target"/>, or the end when null. Returns why it was refused, or null.</summary>
    public string? MoveEntries(IReadOnlyCollection<Guid> ids, Guid grabbed, Guid? target) => state.MoveEntries(ids, grabbed, target);

    /// <summary>Sets an entry's loop count, or null to follow its track. Returns why it was refused, or null.</summary>
    public string? SetEntryLoops(Guid entryId, int? loops) => state.SetEntryLoops(entryId, loops);

    /// <summary>Sets whether Live loops the playlist. Returns why it was refused, or null.</summary>
    public string? SetPlaylistLoops(bool loops) => state.SetPlaylistLoops(loops);

    /// <summary>True when the playlist has something to play.</summary>
    public bool CanGoLive => state.CanGoLive;

    /// <summary>The entry playing while live, or null.</summary>
    public PlaylistEntry? PlayingEntry => state.PlayingEntry;

    /// <summary>The scrub bar's length in seconds.</summary>
    public double ScrubLength => state.ScrubLength;

    /// <summary>A scene track in the world.</summary>
    public Track WorldOf(Track local) => state.WorldOf(local);

    /// <summary>A scene track as the editor shows it: a Follow track's point at its character where they stand now.</summary>
    public Track Shown(Track local) => state.Shown(local);


    /// <summary>True when a track in the world watches or follows a named character who isn't found nearby.</summary>
    public bool TargetLost(Track world) => state.TargetLost(world);

    /// <summary>The aim point on the character a Watch or Follow track in the world names, or null unless found.</summary>
    public Vector3? TargetPoint(Track world) => state.TargetPoint(world);

    /// <summary>Where a track in the world points its camera, or null for a recorded or path aim.</summary>
    public Vector3? AimPoint(Track world) => state.AimPoint(world);

    /// <summary>The selected anchor, or null.</summary>
    public AnchorKind? SelectedAnchor => state.SelectedAnchor;

    /// <summary>The selected anchor in the world, or null.</summary>
    public Anchor? SelectedAnchorInWorld => state.SelectedAnchorInWorld;

    /// <summary>Selects the scene anchor. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor() => state.SelectSceneAnchor();

    /// <summary>Edits a track and selects its anchor, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id) => state.SelectTrackAnchor(id);

    /// <summary>During a live edit, moves the selected anchor. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry) => state.PreviewAnchor(world, carry);

    /// <summary>Edits a track and selects its Look At point. Returns why it was refused, or null.</summary>
    public string? SelectLookAt(Guid id) => state.SelectLookAt(id);

    /// <summary>The selected Look At point in the world, or null.</summary>
    public Vector3? SelectedLookAtInWorld => state.SelectedLookAtInWorld;

    /// <summary>During a live edit, moves the selected Look At point. Returns why it was refused, or null.</summary>
    public string? PreviewLookAt(Vector3 world) => state.PreviewLookAt(world);

    /// <summary>Makes a track the edited one, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SwitchTrack(Guid id) => state.SwitchTrack(id);

    /// <summary>Edits a track and flies the editor camera to its first point, as a point's double-click does. Returns why it was refused, or null.</summary>
    public string? FlyToFirstPoint(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is null) JumpToPoint(0);
        return refusal;
    }

    /// <summary>Edits a track and selects one of its points, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectPoint(Guid track, int index)
    {
        var refusal = state.SwitchTrack(track);
        if (refusal is null) state.Select(index);
        return refusal;
    }

    /// <summary>Read-only view of playback state. Check IsLive before IsPaused or IsFinished.</summary>
    public Director Director => state.Director;

    /// <summary>The characters loaded nearby, as last read.</summary>
    public NearbyCharacters Characters => characters;

    /// <summary>Reads the characters loaded nearby. Call once a frame from Framework.Update.</summary>
    public void RefreshCharacters() => characters.Update(CharacterTable.Read());

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => state.Previewing;

    /// <summary>True while the plugin writes the camera.</summary>
    public bool OwnsCamera => owned;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => state.LocksInput;

    /// <summary>True while a preview or a live shot is running.</summary>
    public bool IsPlaying => state.IsPlaying;

    /// <summary>The free-cam's speed setting.</summary>
    public FlySpeed Speed => freeCam.Speed;

    /// <summary>The free camera's position while editing.</summary>
    public Vector3 CameraPosition { get => freeCam.Position; set => freeCam.Position = value; }

    /// <summary>The free camera's roll in radians.</summary>
    public float CameraRoll { get => freeCam.Roll; set => freeCam.Roll = value; }

    /// <summary>The field of view the camera is looking through, in radians.</summary>
    public float CameraFov { get => freeCam.Fov; set => freeCam.Fov = value; }

    /// <summary>The game's field of view from just before Vista took the camera, or null when Vista does not hold it.</summary>
    public float? TakeoverFov => snapshotBeforeTakeover?.Fov;

    /// <summary>The camera's yaw and pitch, or null when the camera cannot be read.</summary>
    public (float Yaw, float Pitch)? CameraAngles => CameraAccess.ReadAngles();

    /// <summary>Turns the camera, telling the free cam not to read it as a mouse movement.</summary>
    public void TurnCamera(float yaw, float pitch)
    {
        CameraAccess.WriteAngles(yaw, pitch);
        freeCam.Resync();
    }

    /// <summary>True in Off and View, where the game has its camera.</summary>
    public bool Released => state.Released;

    /// <summary>Opens a scene editing its first track, leaving the mode and camera alone. Returns why it was refused, or null.</summary>
    public string? LoadScene(Scene scene) => state.LoadScene(scene);

    /// <summary>Adds a preset as a new track on the ground under the camera and edits it. Returns why it was refused, or null.</summary>
    public string? AddPreset(Preset preset) => state.AddPreset(preset, CameraPosition);

    /// <summary>Track <paramref name="id"/> as a preset.</summary>
    public Preset PresetOf(Guid id) => Presets.From(state.Scene, id);

    /// <summary>Starts free-cam: from Off or View at the game camera, from Live at the current frame. No-op while editing.</summary>
    public void EnterEdit()
    {
        if (state.Mode == CameraMode.Editing) return;

        var start = state.Mode == CameraMode.Live ? lastFrame ?? CameraAccess.ReadState() : CameraAccess.ReadState();
        if (start is null) { Plugin.Log.Error("[vista] cannot read camera state."); return; }

        switch (state.Edit())
        {
            case EditOutcome.FromGame:
                previewedLastFrame = false;
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
        if (state.Stop()) Plugin.Log.Information(editing ? "[vista] preview stopped" : "[vista] paused");
    }

    /// <summary>Stops an Edit preview; the free-cam takes over from the frame shown on the next frame.</summary>
    public void StopPreview() => state.StopPreview();

    /// <summary>Goes to Off, or to View when asked: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason, CameraMode to = CameraMode.Off)
    {
        if (!state.Release(to) && !owned) return;

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

    /// <summary>Applies <paramref name="change"/> to the track if the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change) => state.ChangeTrack(change);

    /// <summary>The selected point's index, or null.</summary>
    public int? Selected => state.Selected;

    /// <summary>Selects a point while editing; null or out of range clears the selection.</summary>
    public void Select(int? index) => state.Select(index);

    /// <summary>The selected timing key's index, or null.</summary>
    public int? SelectedKey => state.SelectedKey;

    /// <summary>The selected leg, or null.</summary>
    public int? SelectedLeg => state.SelectedLeg;

    /// <summary>Selects a timing key while editing; a point's key also selects its point.</summary>
    public void SelectKey(int? key) => state.SelectKey(key);

    /// <summary>Selects a leg while editing, leaving the point selection alone.</summary>
    public void SelectLeg(int? leg) => state.SelectLeg(leg);

    /// <summary>The evaluator for the edited track.</summary>
    public TrackEvaluator Evaluator => state.Evaluator;

    /// <summary>Sets the track's speed. Returns why it was refused, or null.</summary>
    public string? SetTrackSpeed(float speed) => state.SetTrackSpeed(speed);

    /// <summary>Sets the track's speed so the shot takes about the given seconds. Returns why it was refused, or null.</summary>
    public string? SetTrackDuration(float seconds) => state.SetTrackDuration(seconds);

    /// <summary>Pins a leg at the speed that takes the given seconds. Returns why it was refused, or null.</summary>
    public string? SetLegDuration(int leg, float seconds) => state.SetLegDuration(leg, seconds);

    /// <summary>Pins a leg at a speed. Returns why it was refused, or null.</summary>
    public string? SetLegSpeed(int leg, float speed) => state.SetLegSpeed(leg, speed);

    /// <summary>Unpins a leg so it follows the track speed again. Returns why it was refused, or null.</summary>
    public string? ResetLeg(int leg) => state.ResetLeg(leg);

    /// <summary>Sets a leg's easing. Returns why it was refused, or null.</summary>
    public string? SetEasing(int leg, Easing easing) => state.SetEasing(leg, easing);

    /// <summary>Sets a key's sides to Auto, Linear or Flat. Returns why it was refused, or null.</summary>
    public string? SetKeyMode(int key, TangentMode mode) => state.SetKeyMode(key, mode);

    /// <summary>Removes the hold a hold end closes. Returns why it was refused, or null.</summary>
    public string? RemoveHold(int key) => state.RemoveHold(key);

    /// <summary>Lets a key's handles move separately. Returns why it was refused, or null.</summary>
    public string? BreakHandles(int key) => state.BreakHandles(key);

    /// <summary>Joins a key's handles at the <paramref name="from"/> side's slope. Returns why it was refused, or null.</summary>
    public string? UnifyHandles(int key, KeySide from) => state.UnifyHandles(key, from);

    /// <summary>Sets the aim mode; the first Look At with no points goes ahead of the camera. Returns why it was refused, or null.</summary>
    public string? SetAim(AimMode aim)
    {
        if (CameraPoint() is { } camera) return state.SetAim(aim, camera);
        var placesFromCamera = aim == AimMode.LookAt && state.Track is { Points.Count: 0, LookAtPlaced: false };
        return state.Mode == CameraMode.Editing && placesFromCamera ? "Cannot read the camera." : state.SetAim(aim, new ControlPoint(Vector3.Zero, 0f, 0f, 1f));
    }

    /// <summary>Names the character to watch or follow by name and home world, or none. Returns why it was refused, or null.</summary>
    public string? SetTarget(string? name, string? world) => state.SetTarget(name, world);

    /// <summary>Sets how heavily the aim eases onto the character. Returns why it was refused, or null.</summary>
    public string? SetSmoothing(float smoothing) => state.SetSmoothing(smoothing);

    /// <summary>Sets whether a Follow track's offset turns with its character. Returns why it was refused, or null.</summary>
    public string? SetFollowTurns(bool turns) => state.SetFollowTurns(turns);

    /// <summary>Sets whether a Follow track's camera looks at its character. Returns why it was refused, or null.</summary>
    public string? SetFollowLooks(bool looks) => state.SetFollowLooks(looks);

    /// <summary>During a live edit, drags a key towards a time. Returns why it was refused, or null.</summary>
    public string? PreviewKeyMove(int key, float time, bool ripple = false) => state.PreviewKeyMove(key, time, ripple);

    /// <summary>During a live edit, sets a handle's slope in distance per second. Returns why it was refused, or null.</summary>
    public string? PreviewHandle(int key, KeySide side, float distancePerSecond) => state.PreviewHandle(key, side, distancePerSecond);

    /// <summary>The track's length in seconds.</summary>
    public double Duration => state.Duration;

    /// <summary>True while the scrub head is being dragged.</summary>
    public bool Scrubbing => state.Scrubbing;

    /// <summary>Seconds under the scrub head.</summary>
    public double ScrubHead => state.ScrubHead;

    /// <summary>Starts dragging the scrub head; while editing the camera shows the scrubbed frame.</summary>
    public void BeginScrub() => state.BeginScrub();

    /// <summary>Moves the scrub head; live, playback seeks there.</summary>
    public void ScrubTo(double time) => state.ScrubTo(time);

    /// <summary>Stops dragging the scrub head; while editing the free-cam flies on from the frame shown.</summary>
    public void FinishScrub()
    {
        var fromEditing = state.Mode == CameraMode.Editing && state.Scrubbing;
        state.EndScrub();
        if (fromEditing && state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }

    /// <summary>Puts the free-cam at point <paramref name="index"/> while editing, as a scrub release would.</summary>
    public void JumpToPoint(int index)
    {
        if (state.Mode != CameraMode.Editing || index < 0 || index >= state.Track.Points.Count) return;
        state.ScrubTo(state.Evaluator.PointSeconds(index));
        if (state.FrameAt(state.ScrubHead) is { } frame) FlyFrom(frame);
    }

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => state.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => state.CanRedo;

    /// <summary>Restores the scene, the edited track and the selection before the last change.</summary>
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

    /// <summary>Moves points as a block onto <paramref name="target"/>, or the end when null. Returns why it was refused, or null.</summary>
    public string? MovePoints(IReadOnlyCollection<int> points, int grabbed, int? target) => state.MovePoints(points, grabbed, target);

    /// <summary>Starts a live edit that previews on the track and ends as one undo step.</summary>
    public void BeginLiveEdit() => state.BeginLiveEdit();

    /// <summary>Replaces point <paramref name="index"/> during a live edit. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point) => state.PreviewPoint(index, point);

    /// <summary>Ends a live edit as one undo step if anything changed.</summary>
    public void EndLiveEdit() => state.EndLiveEdit();

    /// <summary>During a live edit, sets the aim height. Returns why it was refused, or null.</summary>
    public string? PreviewAimHeight(float yalms) => state.PreviewAimHeight(yalms);

    /// <summary>The edited Follow Target track's offset as an orbit round its character, or null unless it follows with its one point.</summary>
    public Orbit? FollowOrbit => state.FollowOrbit;

    /// <summary>During a live edit, moves the Follow Target point to <paramref name="orbit"/>. Returns why it was refused, or null.</summary>
    public string? PreviewFollowOrbit(Orbit orbit) => state.PreviewFollowOrbit(orbit);

    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point, or the previewed frame while previewing.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be added while editing.";
        if (state.Scrubbing) return "Points cannot be added while scrubbing.";
        return CameraPoint() is { } point ? edit(point) : "Cannot read the camera.";
    }

    /// <summary>The current camera as a control point, or the previewed frame while previewing; null when the camera can't be read.</summary>
    private ControlPoint? CameraPoint()
    {
        if (state.Previewing && lastFrame is { } previewed)
        {
            var (previewYaw, previewPitch) = TrackAim.FromDirection(previewed.LookAt - previewed.Position);
            return new ControlPoint(previewed.Position, previewYaw, previewPitch, previewed.Fov, previewed.Roll);
        }

        var camera = CameraAccess.ReadState();
        var angles = CameraAccess.ReadAngles();
        if (camera is null || angles is null) return null;

        var (yaw, pitch) = angles.Value;
        return new ControlPoint(camera.Value.Position, yaw, pitch, camera.Value.Fov, freeCam.Roll);
    }

    /// <summary>Where the camera goes this frame, or null to leave it to the game. Called from the camera hook.</summary>
    public CameraState? Frame(float dt)
    {
        if (!owned) return null;

        var frame = state.Mode switch
        {
            CameraMode.Editing => EditingFrame(dt),
            CameraMode.Live => state.Director.Tick(dt),
            _ => null,
        };

        lastFrame = frame;
        return frame;
    }

    /// <summary>While editing: the preview's frame, the scrubbed frame, or the free-cam, handing the free-cam the last frame when a preview stops.</summary>
    private CameraState? EditingFrame(float dt)
    {
        if (state.Previewing && FreeCam.HasFlightInput()) state.StopPreview();
        var frame = state.AdvancePreview(dt);

        if (previewedLastFrame && !state.Previewing)
        {
            previewedLastFrame = false;
            if ((frame ?? lastFrame ?? state.FrameAt(state.ScrubHead)) is { } last) FlyFrom(last);
            return freeCam.Tick(dt);
        }

        previewedLastFrame = state.Previewing;
        if (frame is { } previewing) return previewing;
        return state.Scrubbing && state.FrameAt(state.ScrubHead) is { } scrubbed ? scrubbed : freeCam.Tick(dt);
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
                Plugin.Log.Debug(previewRefusal
                    ? "[vista] cannot preview a track with no points."
                    : "[vista] nothing to play: add a track with points to the playlist.");
                return;
            case PlayOutcome.ReHid:
                if (hideUiInLive) GameUi.Hide();
                return;
            case PlayOutcome.Resumed:
                if (hideUiInLive) GameUi.Hide();
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

        if (hideUiInLive) GameUi.Hide();
        Plugin.Log.Information("[vista] mode: live, {Count} playlist entries", state.Scene.Playlist.Count);
    }

    /// <summary>Takes the camera, remembering what to put back on release.</summary>
    private void TakeCamera()
    {
        snapshotBeforeTakeover ??= CameraAccess.Capture();
        owned = true;
    }

    /// <summary>Puts the free-cam at <paramref name="frame"/>, keeping its aim by writing the game's yaw and pitch within its limits.</summary>
    private void FlyFrom(CameraState frame)
    {
        previewedLastFrame = false;
        freeCam.Enable(frame.Position, frame.Roll, frame.Fov);
        var (yaw, pitch) = TrackAim.FromDirection(frame.LookAt - frame.Position);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        CameraAccess.WriteAngles(yaw, Math.Clamp(pitch, min, max));
    }
}
