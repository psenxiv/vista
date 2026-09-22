using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Plugin.Game;

namespace Vista.Plugin.Session;

/// <summary>Carries out the session's mode changes in game: free-cam, movement lock, camera ownership and UI.</summary>
internal sealed class CameraSession
{
    private readonly SessionState state = new(Ground.Below);
    private readonly FreeCam freeCam = new();
    private readonly MovementLock movement;
    private readonly CameraOwnership ownership = new();
    private CameraAccess.Snapshot? snapshotBeforeTakeover;
    private CameraState? lastFrame;
    private bool previewedLastFrame;

    public CameraSession(MovementLock movement) => this.movement = movement;

    public CameraMode Mode => state.Mode;

    /// <summary>The edited track: Edit builds it and a preview plays it. Changed only through the edit methods and undo.</summary>
    public Track Track => state.Track;

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

    /// <summary>Deletes a track. Returns why it was refused, or null.</summary>
    public string? DeleteTrack(Guid id) => state.DeleteTrack(id);

    /// <summary>Moves a track in the Hierarchy order. Returns why it was refused, or null.</summary>
    public string? MoveTrack(int from, int to) => state.MoveTrack(from, to);

    /// <summary>Hides or shows a track. Returns why it was refused, or null.</summary>
    public string? SetTrackHidden(Guid id, bool hidden) => state.SetTrackHidden(id, hidden);

    /// <summary>Adds a playlist entry for a track. Returns why it was refused, or null.</summary>
    public string? AddToPlaylist(Guid trackId, int? index = null) => state.AddToPlaylist(trackId, index);

    /// <summary>Removes a playlist entry. Returns why it was refused, or null.</summary>
    public string? RemoveFromPlaylist(Guid entryId) => state.RemoveFromPlaylist(entryId);

    /// <summary>Moves a playlist entry. Returns why it was refused, or null.</summary>
    public string? MovePlaylistEntry(int from, int to) => state.MovePlaylistEntry(from, to);

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

    /// <summary>The selected anchor, or null.</summary>
    public AnchorKind? SelectedAnchor => state.SelectedAnchor;

    /// <summary>The selected anchor in the world, or null.</summary>
    public Anchor? SelectedAnchorInWorld => state.SelectedAnchorInWorld;

    /// <summary>Selects the scene anchor. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor() => state.SelectSceneAnchor();

    /// <summary>Edits a track and selects its anchor, leaving the camera where it is. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id) => state.SelectTrackAnchor(id);

    /// <summary>Moves the selected anchor. Returns why it was refused, or null.</summary>
    public string? MoveAnchor(Anchor world, bool carry) => state.MoveAnchor(world, carry);

    /// <summary>During a live edit, moves the selected anchor. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry) => state.PreviewAnchor(world, carry);

    /// <summary>Edits a track and flies the editor camera to look at its anchor. Returns why it was refused, or null.</summary>
    public string? OpenTrack(Guid id)
    {
        var refusal = state.SwitchTrack(id);
        if (refusal is not null) return refusal;

        var track = SceneEditing.Get(state.Scene, id);
        if (!track.AnchorPlaced) return null;
        var (position, lookAt) = SceneGeometry.ViewOf(SceneGeometry.WorldAnchor(state.Scene, track));
        var fov = CameraAccess.ReadState()?.Fov ?? lastFrame?.Fov ?? 1f;
        state.StopPreview();
        FlyFrom(new CameraState(position, lookAt, fov));
        return null;
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

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => state.Previewing;

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
        if (start is null) { Plugin.Log.Error("[vista] cannot read camera state."); return; }

        switch (state.Edit())
        {
            case EditOutcome.FromOff:
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
    public void Play()
    {
        var previewing = state.Mode == CameraMode.Editing;
        Apply(state.Play(), previewing);
    }

    /// <summary>In Edit, previews from the beginning; otherwise goes live with the playlist from the start, taking the camera if off. Refused when nothing can play.</summary>
    public void Restart()
    {
        var previewing = state.Mode == CameraMode.Editing;
        Apply(state.Restart(), previewing);
    }

    /// <summary>Goes live with the playlist paused at its start, leaving the UI shown. Refused when nothing can play.</summary>
    public void Cue() => Apply(state.Cue());

    /// <summary>Live, holds the current frame; in Edit, stops a preview.</summary>
    public void Stop()
    {
        var editing = state.Mode == CameraMode.Editing;
        if (state.Stop()) Plugin.Log.Information(editing ? "[vista] preview stopped" : "[vista] paused");
    }

    /// <summary>Stops an Edit preview; the free-cam takes over from the frame shown on the next frame.</summary>
    public void StopPreview() => state.StopPreview();

    /// <summary>Turns the plugin off: stops playback and free-cam, unlocks, and hands the camera back.</summary>
    public void Release(string reason)
    {
        if (!state.Release() && !ownership.IsOwned) return;

        freeCam.Disable();
        GameUi.Restore();
        movement.Release();
        lastFrame = null;
        previewedLastFrame = false;
        ownership.Release(reason);

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

    /// <summary>During a live edit, drags a key towards a time. Returns why it was refused, or null.</summary>
    public string? PreviewKeyMove(int key, float time) => state.PreviewKeyMove(key, time);

    /// <summary>During a live edit, sets a handle's slope in distance per second. Returns why it was refused, or null.</summary>
    public string? PreviewHandle(int key, KeySide side, float distancePerSecond) => state.PreviewHandle(key, side, distancePerSecond);

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

    /// <summary>Moves a point in the order. Returns why it was refused, or null.</summary>
    public string? MovePoint(int from, int to) => state.MovePoint(from, to);

    /// <summary>Starts a live edit that previews on the track and ends as one undo step.</summary>
    public void BeginLiveEdit() => state.BeginLiveEdit();

    /// <summary>Replaces point <paramref name="index"/> during a live edit. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point) => state.PreviewPoint(index, point);

    /// <summary>Ends a live edit as one undo step if anything changed.</summary>
    public void EndLiveEdit() => state.EndLiveEdit();

    /// <summary>Runs <paramref name="edit"/> with the current camera as a control point, or the previewed frame while previewing.</summary>
    private string? WithCurrentPoint(Func<ControlPoint, string?> edit)
    {
        if (state.Mode != CameraMode.Editing) return "Points can only be added while editing.";
        if (state.Scrubbing) return "Points cannot be added while scrubbing.";

        if (state.Previewing && lastFrame is { } previewed)
        {
            var (previewYaw, previewPitch) = TrackAim.FromDirection(previewed.LookAt - previewed.Position);
            return edit(new ControlPoint(previewed.Position, previewYaw, previewPitch, previewed.Fov, previewed.Roll));
        }

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
                Plugin.Log.Error(previewRefusal
                    ? "[vista] cannot preview a track with no points."
                    : "[vista] nothing to play: add a track with points to the playlist.");
                return;
            case PlayOutcome.ReHid:
                GameUi.Hide();
                return;
            case PlayOutcome.Resumed:
                GameUi.Hide();
                Plugin.Log.Information("[vista] resumed");
                return;
            case PlayOutcome.StartedFromOff or PlayOutcome.CuedFromOff:
                movement.Hold();
                TakeCamera();
                break;
        }

        freeCam.Disable();
        if (outcome is PlayOutcome.Cued or PlayOutcome.CuedFromOff)
        {
            Plugin.Log.Information("[vista] mode: live, cued, {Count} playlist entries", state.Scene.Playlist.Count);
            return;
        }

        GameUi.Hide();
        Plugin.Log.Information("[vista] mode: live, {Count} playlist entries", state.Scene.Playlist.Count);
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
        previewedLastFrame = false;
        freeCam.Enable(frame.Position, frame.Roll, frame.Fov);
        var (yaw, pitch) = TrackAim.FromDirection(frame.LookAt - frame.Position);
        var (min, max) = CameraAccess.ReadPitchLimits() ?? (-MathF.PI / 2f, MathF.PI / 2f);
        CameraAccess.WriteAngles(yaw, Math.Clamp(pitch, min, max));
    }
}
