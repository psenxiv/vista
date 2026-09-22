using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;

namespace Vista.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome { Unchanged, FromOff, FromLive }

/// <summary>What <see cref="SessionState.Play"/>, <see cref="SessionState.Restart"/> or <see cref="SessionState.Cue"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff, Cued, CuedFromOff, Previewed }

/// <summary>The mode, the Director, the scene and the edited track, and the rules for moving between modes.</summary>
public sealed class SessionState
{
    private readonly EditHistory history = new();
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;
    private double scrubTime;
    private bool resumeAfterScrub;
    private EditSnapshot? liveEditStart;
    private Track? evaluatedStart;
    private TrackEvaluator? liveStartEvaluator;
    private readonly Func<float?> footHeight;
    private readonly Dictionary<Guid, (Track Local, Anchor Scene, Track World)> worlds = new();
    private TrackPlayback? preview;

    /// <summary>True while an Edit preview is playing.</summary>
    public bool Previewing => preview is not null;

    public CameraMode Mode { get; private set; }

    public Director Director { get; } = new();

    /// <summary>The tracks being edited, their order and which are hidden.</summary>
    public Scene Scene { get; private set; } = SceneEditing.New();

    /// <summary>The Id of the track the editor works on.</summary>
    public Guid EditedTrackId { get; private set; }

    /// <summary>The edited track as stored, local to its anchor.</summary>
    private Track Local
    {
        get => SceneEditing.Get(Scene, EditedTrackId);
        set => Scene = SceneEditing.Replace(Scene, value);
    }

    /// <summary>The edited track in the world: Edit builds it and Play plays it. Changed only through the edit methods and undo.</summary>
    public Track Track => WorldOf(Local);

    /// <summary>A scene track in the world; the same instance until the track or the scene anchor changes.</summary>
    public Track WorldOf(Track local)
    {
        if (worlds.TryGetValue(local.Id, out var cached) && ReferenceEquals(cached.Local, local) && cached.Scene == Scene.Anchor) return cached.World;
        var world = SceneGeometry.InWorld(Scene, local);
        worlds[local.Id] = (local, Scene.Anchor, world);
        return world;
    }

    /// <summary>A session; <paramref name="footHeight"/> reads the character's feet, or null when it can't.</summary>
    public SessionState(Func<float?>? footHeight = null)
    {
        this.footHeight = footHeight ?? (() => null);
        EditedTrackId = Scene.Tracks[0].Id;
    }

    /// <summary>The track's length in seconds: its last compiled key, or 0 with no points.</summary>
    public double Duration => Evaluator.Duration;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => Mode != CameraMode.Off;

    /// <summary>Enters editing; from live, takes the Director offline.</summary>
    public EditOutcome Edit()
    {
        StopPreview();
        switch (Mode)
        {
            case CameraMode.Editing:
                return EditOutcome.Unchanged;
            case CameraMode.Live:
                Scrubbing = false;
                scrubTime = Math.Clamp(Director.ShotTime, 0.0, Duration);
                Director.GoOffline();
                Mode = CameraMode.Editing;
                return EditOutcome.FromLive;
            default:
                Scrubbing = false;
                Mode = CameraMode.Editing;
                return EditOutcome.FromOff;
        }
    }

    /// <summary>In Edit, previews from the scrub head; live, resumes a paused shot or leaves a playing one alone; otherwise goes live.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Editing) return preview is not null ? PlayOutcome.Previewed : StartPreview(fromStart: false);
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused) return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return GoLive();
    }

    /// <summary>In Edit, previews from the beginning; otherwise goes live from the start. Refused with no points.</summary>
    public PlayOutcome Restart() => Mode == CameraMode.Editing ? StartPreview(fromStart: true) : GoLive();

    /// <summary>Goes live with the track paused at its start. Refused with no points.</summary>
    public PlayOutcome Cue()
    {
        var outcome = GoLive();
        if (outcome == PlayOutcome.Refused) return outcome;
        Director.Pause();
        return outcome == PlayOutcome.StartedFromOff ? PlayOutcome.CuedFromOff : PlayOutcome.Cued;
    }

    /// <summary>Live, holds the current frame; in Edit, stops a preview. Returns false when there was nothing to stop.</summary>
    public bool Stop()
    {
        if (Mode == CameraMode.Editing) return StopPreview();
        if (Mode != CameraMode.Live) return false;
        Director.Pause();
        return true;
    }

    /// <summary>Advances an Edit preview, stopping it at the end of a cycle that doesn't loop. Returns its frame, or null when not previewing.</summary>
    public CameraState? AdvancePreview(float dt)
    {
        if (preview is not { } playback) return null;
        var frame = playback.Advance(dt);
        if (playback.IsFinished) StopPreview();
        return frame;
    }

    /// <summary>Stops an Edit preview, leaving the scrub head at its shot time. Returns false if none was playing.</summary>
    public bool StopPreview()
    {
        if (preview is not { } playback) return false;
        scrubTime = playback.ShotTime;
        preview = null;
        return true;
    }

    /// <summary>Goes live with the track from its start. Refused with no points.</summary>
    private PlayOutcome GoLive()
    {
        if (Local.Points.Count == 0) return PlayOutcome.Refused;
        StopPreview();
        Scrubbing = false;
        EndLiveEdit();

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
    }

    /// <summary>Starts an Edit preview from the scrub head, or from the beginning when asked or when the scrub head is where the shot finishes.</summary>
    private PlayOutcome StartPreview(bool fromStart)
    {
        if (Local.Points.Count == 0) return PlayOutcome.Refused;
        EndLiveEdit();
        Scrubbing = false;

        var playback = new TrackPlayback(Track);
        if (!fromStart)
        {
            playback.Seek(ScrubHead);
            if (playback.IsFinished) playback.Restart();
        }

        preview = playback;
        return PlayOutcome.Previewed;
    }

    /// <summary>Turns off and takes the Director offline. Returns false if already off.</summary>
    public bool Release()
    {
        StopPreview();
        if (Mode == CameraMode.Off) return false;
        Scrubbing = false;
        EndLiveEdit();
        Director.GoOffline();
        Mode = CameraMode.Off;
        return true;
    }

    /// <summary>The selected point's index, or null.</summary>
    public int? Selected { get; private set; }

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => Mode == CameraMode.Editing && history.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => Mode == CameraMode.Editing && history.CanRedo;

    /// <summary>The selected timing key's index, or null. Never set together with <see cref="SelectedLeg"/>.</summary>
    public int? SelectedKey { get; private set; }

    /// <summary>The selected leg, or null. Never set together with <see cref="SelectedKey"/>.</summary>
    public int? SelectedLeg { get; private set; }

    /// <summary>Selects a point while editing; null or an index out of range clears the selection.</summary>
    public void Select(int? index)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedAnchor = null;
        Selected = index is { } i && i >= 0 && i < Local.Points.Count ? i : null;
        SyncKeyToPoint();
    }

    /// <summary>Selects a timing key while editing; a point's key also selects its point.</summary>
    public void SelectKey(int? key)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedLeg = null;
        SelectedKey = key is { } k && k >= 0 && k < TrackEditing.KeyCount(Local) ? k : null;
        if (SelectedKey is { } s && TrackEditing.RoleOf(Local, s) == KeyRole.Point)
        {
            Selected = TrackEditing.PointOf(Local, s);
            SelectedAnchor = null;
        }
    }

    /// <summary>Selects a leg while editing, leaving the point selection alone.</summary>
    public void SelectLeg(int? leg)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedKey = null;
        SelectedLeg = leg is { } l && l >= 1 && l < Local.Points.Count ? l : null;
    }

    /// <summary>Applies <paramref name="change"/> if editing and the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change)
        => Apply(change, result => Selected is { } s && s < result.Points.Count ? s : null);

    /// <summary>Appends a world point, placing the anchors under a first point; the selection is unchanged.</summary>
    public string? AddToEnd(ControlPoint point)
        => ApplyScene(scene => WithPoint(scene, point, TrackEditing.Append), _ => Selected);

    /// <summary>Inserts a world point after the selected one and selects it.</summary>
    public string? AddAfterSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        var s = Selected!.Value;
        return ApplyScene(scene => WithPoint(scene, point, (t, p) => TrackEditing.InsertAfter(t, s, p)), _ => s + 1);
    }

    /// <summary>Replaces the selected point, keeping its timing and the selection.</summary>
    public string? OverwriteSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        return ReplacePoint(Selected!.Value, point);
    }

    /// <summary>Replaces point <paramref name="index"/> with a world point, keeping its timing and the selection.</summary>
    public string? ReplacePoint(int index, ControlPoint point)
        => Apply(t => TrackEditing.Replace(t, index, ToLocal(point)), _ => Selected);

    /// <summary>A world point as the edited track stores it.</summary>
    private ControlPoint ToLocal(ControlPoint world) => SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world);

    /// <summary>Places the anchors under a first point if needed, then adds the world point to the edited track with <paramref name="add"/>.</summary>
    private Scene WithPoint(Scene scene, ControlPoint world, Func<Track, ControlPoint, Track> add)
    {
        var placed = SceneGeometry.PlaceFor(scene, EditedTrackId, world.Position, footHeight() ?? world.Position.Y);
        var track = SceneEditing.Get(placed, EditedTrackId);
        return SceneEditing.Replace(placed, add(track, SceneGeometry.WorldAnchor(placed, track).ToLocal(world)));
    }

    /// <summary>Deletes the selected point and clears the selection.</summary>
    public string? DeleteSelected()
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        return DeletePoint(Selected!.Value);
    }

    /// <summary>Deletes point <paramref name="index"/>; any other selected point stays selected.</summary>
    public string? DeletePoint(int index)
    {
        if (index < 0 || index >= Local.Points.Count) return "There is no such point.";
        var selected = Selected;
        return Apply(t => TrackEditing.Delete(t, index), _ => selected is { } s && s != index ? (s > index ? s - 1 : s) : null);
    }

    /// <summary>Moves a point in the order; the selection stays on the same point.</summary>
    public string? MovePoint(int from, int to)
    {
        var selected = Selected;
        return Apply(t => TrackEditing.Move(t, from, to), _ => selected is { } s ? Follow(s, from, to) : null);
    }

    /// <summary>Restores the scene, the edited track and the selection before the last change. Returns false if nothing was undone.</summary>
    public bool Undo()
    {
        StopPreview();
        EndLiveEdit();
        return Restore(Mode == CameraMode.Editing ? history.Undo(Current) : null);
    }

    /// <summary>Re-applies the last undone change. Returns false if nothing was redone.</summary>
    public bool Redo()
    {
        StopPreview();
        EndLiveEdit();
        return Restore(Mode == CameraMode.Editing ? history.Redo(Current) : null);
    }

    /// <summary>Sets the track's speed. Returns why it was refused, or null.</summary>
    public string? SetTrackSpeed(float speed) => ApplyTiming(t => TrackEditing.SetSpeed(t, EditLimits.Speed(speed)));

    /// <summary>Sets the track's speed so the shot takes about <paramref name="seconds"/>. Returns why it was refused, or null.</summary>
    public string? SetTrackDuration(float seconds) => ApplyTiming(t => TrackEditing.SetDuration(t, EditLimits.ShotDuration(seconds)));

    /// <summary>Pins leg <paramref name="leg"/> at the speed that takes <paramref name="seconds"/>. Returns why it was refused, or null.</summary>
    public string? SetLegDuration(int leg, float seconds) => ApplyTiming(t => TrackEditing.SetLegDuration(t, leg, EditLimits.Leg(seconds)));

    /// <summary>Pins leg <paramref name="leg"/> at <paramref name="speed"/>. Returns why it was refused, or null.</summary>
    public string? SetLegSpeed(int leg, float speed) => ApplyTiming(t => TrackEditing.SetLegSpeed(t, leg, EditLimits.Speed(speed)));

    /// <summary>Unpins leg <paramref name="leg"/> so it follows the track speed again. Returns why it was refused, or null.</summary>
    public string? ResetLeg(int leg) => ApplyTiming(t => TrackEditing.ResetLeg(t, leg));

    /// <summary>Sets leg <paramref name="leg"/>'s easing. Returns why it was refused, or null.</summary>
    public string? SetEasing(int leg, Easing easing) => ApplyTiming(t => LegEasing.Set(t, leg, easing));

    /// <summary>Sets key <paramref name="key"/>'s sides to Auto, Linear or Flat. Returns why it was refused, or null.</summary>
    public string? SetKeyMode(int key, TangentMode mode) => ApplyTiming(t => TimingEditing.SetKeyMode(t, key, mode));

    /// <summary>Removes the hold a hold end closes, clearing the timing selection. Returns why it was refused, or null.</summary>
    public string? RemoveHold(int key)
    {
        var refusal = ApplyTiming(t => TimingEditing.RemoveHold(t, key));
        if (refusal is null) SelectedKey = null;
        return refusal;
    }

    /// <summary>Lets key <paramref name="key"/>'s handles move separately. Returns why it was refused, or null.</summary>
    public string? BreakHandles(int key) => ApplyTiming(t => TimingEditing.SetBroken(t, key, true));

    /// <summary>Joins key <paramref name="key"/>'s handles at the slope the <paramref name="from"/> side has in the graph. Returns why it was refused, or null.</summary>
    public string? UnifyHandles(int key, KeySide from)
        => ApplyTiming(t =>
        {
            var joined = TimingEditing.SetBroken(t, key, false);
            return Collinear(joined, Evaluator, key, Evaluator.SideSlope(key, from), null);
        });

    /// <summary>Adds an empty track at the end and edits it. Returns why it was refused, or null.</summary>
    public string? AddTrack() => CommitScene(scene => SceneEditing.Add(scene));

    /// <summary>Renames track <paramref name="id"/>. Returns why it was refused, or null.</summary>
    public string? RenameTrack(Guid id, string name) => CommitScene(scene => (SceneEditing.Rename(scene, id, name), EditedTrackId));

    /// <summary>Copies track <paramref name="id"/> after itself and edits the copy. Returns why it was refused, or null.</summary>
    public string? DuplicateTrack(Guid id) => CommitScene(scene => SceneEditing.Duplicate(scene, id));

    /// <summary>Deletes track <paramref name="id"/>; deleting the edited track edits the one taking its place, shown if it was hidden. Returns why it was refused, or null.</summary>
    public string? DeleteTrack(Guid id)
        => CommitScene(scene =>
        {
            var (result, next) = SceneEditing.Delete(scene, id);
            if (id == EditedTrackId && result.Hidden.Contains(next)) result = SceneEditing.SetHidden(result, next, false);
            return (result, id == EditedTrackId ? next : EditedTrackId);
        });

    /// <summary>Moves a track in the Hierarchy order. Returns why it was refused, or null.</summary>
    public string? MoveTrack(int from, int to) => CommitScene(scene => (SceneEditing.Move(scene, from, to), EditedTrackId));

    /// <summary>Hides or shows track <paramref name="id"/>; the edited track is always shown. Returns why it was refused, or null.</summary>
    public string? SetTrackHidden(Guid id, bool hidden)
    {
        if (hidden && id == EditedTrackId) return "The track being edited is always shown.";
        return CommitScene(scene => (SceneEditing.SetHidden(scene, id, hidden), EditedTrackId));
    }

    /// <summary>Edits track <paramref name="id"/>, showing it first if hidden. Not an undo step itself. Returns why it was refused, or null.</summary>
    public string? SwitchTrack(Guid id)
    {
        if (Mode != CameraMode.Editing) return "Tracks can only be switched while editing.";
        if (SceneEditing.IndexOf(Scene, id) < 0) return "There is no such track.";
        if (Scene.Hidden.Contains(id) && SetTrackHidden(id, false) is { } refusal) return refusal;
        if (id == EditedTrackId) return null;

        StopPreview();
        EndLiveEdit();
        ClearForSwitch();
        EditedTrackId = id;
        return null;
    }

    /// <summary>Starts a live edit: previews change the track at once and end as one undo step. Editing only.</summary>
    public void BeginLiveEdit()
    {
        StopPreview();
        if (Mode == CameraMode.Editing && liveEditStart is null) liveEditStart = Current;
    }

    /// <summary>Replaces point <paramref name="index"/> during a live edit without recording a step. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point)
    {
        if (liveEditStart is null) return "No live edit is in progress.";
        try
        {
            var result = TrackEditing.Replace(Local, index, ToLocal(point));
            _ = new TrackEvaluator(result);
            Local = result;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>During a live edit, drags key <paramref name="key"/> towards <paramref name="time"/> from the track as the edit began.</summary>
    public string? PreviewKeyMove(int key, float time)
        => PreviewFromStart((start, evaluator) => TimingEditing.MoveKey(start, evaluator, key, time));

    /// <summary>During a live edit, sets a handle to a slope in distance per second, both sides unless the key is broken.</summary>
    public string? PreviewHandle(int key, KeySide side, float distancePerSecond)
        => PreviewFromStart((start, evaluator) => Collinear(start, evaluator, key, distancePerSecond, start.Timing[TrackEditing.PointOf(start, key)].Broken ? side : null));

    /// <summary>Ends a live edit, recording it as one undo step if the scene changed.</summary>
    public void EndLiveEdit()
    {
        if (liveEditStart is not { } start) return;
        liveEditStart = null;
        if (ReferenceEquals(start.Scene, Scene)) return;

        // Previews rebuild the lists, so compare values: a drag back to the start is no step.
        if (SameValues(start.Scene, Scene)) Scene = start.Scene;
        else history.Record(start);
    }

    /// <summary>True when two scenes hold the same anchor, hidden set and tracks by value.</summary>
    private static bool SameValues(Scene a, Scene b)
    {
        if (a.Anchor != b.Anchor || a.AnchorPlaced != b.AnchorPlaced || a.Tracks.Count != b.Tracks.Count || !a.Hidden.SetEquals(b.Hidden)) return false;
        for (var i = 0; i < a.Tracks.Count; i++)
        {
            var x = a.Tracks[i];
            var y = b.Tracks[i];
            if (ReferenceEquals(x, y)) continue;
            if (x with { Points = y.Points, Timing = y.Timing } != y
                || !x.Points.SequenceEqual(y.Points) || !x.Timing.SequenceEqual(y.Timing)) return false;
        }

        return true;
    }

    /// <summary>The selected anchor, or null; never set together with a selected point.</summary>
    public AnchorKind? SelectedAnchor { get; private set; }

    /// <summary>The selected anchor in the world, or null.</summary>
    public Anchor? SelectedAnchorInWorld => SelectedAnchor switch
    {
        AnchorKind.Scene => Scene.Anchor,
        AnchorKind.Track => SceneGeometry.WorldAnchor(Scene, Local),
        _ => null,
    };

    /// <summary>Selects the scene anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectSceneAnchor()
    {
        if (Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (UnplacedRefusal(AnchorKind.Scene) is { } unplaced) return unplaced;
        SelectAnchor(AnchorKind.Scene);
        return null;
    }

    /// <summary>Edits track <paramref name="id"/> and selects its anchor, clearing any point. Returns why it was refused, or null.</summary>
    public string? SelectTrackAnchor(Guid id)
    {
        if (Mode != CameraMode.Editing) return "Anchors can only be selected while editing.";
        if (SceneEditing.IndexOf(Scene, id) < 0) return "There is no such track.";
        if (!SceneEditing.Get(Scene, id).AnchorPlaced) return TrackAnchorUnplaced;
        if (SwitchTrack(id) is { } refusal) return refusal;
        SelectAnchor(AnchorKind.Track);
        return null;
    }

    private const string SceneAnchorUnplaced = "The scene anchor is placed with the scene's first point.";
    private const string TrackAnchorUnplaced = "A track's anchor is placed with its first point.";

    /// <summary>Why the <paramref name="kind"/> anchor cannot be used yet because it is unplaced, or null.</summary>
    private string? UnplacedRefusal(AnchorKind kind)
        => kind == AnchorKind.Scene
            ? (Scene.AnchorPlaced ? null : SceneAnchorUnplaced)
            : (Local.AnchorPlaced ? null : TrackAnchorUnplaced);

    private void SelectAnchor(AnchorKind kind)
    {
        EndLiveEdit();
        Selected = null;
        SyncKeyToPoint();
        SelectedAnchor = kind;
    }

    /// <summary>Moves the selected anchor in the world, carrying what hangs off it or alone. Returns why it was refused, or null.</summary>
    public string? MoveAnchor(Anchor world, bool carry)
    {
        if (SelectedAnchor is not { } kind) return "Select an anchor first.";
        if (UnplacedRefusal(kind) is { } unplaced) return unplaced;
        return CommitScene(scene => (Moved(scene, kind, world, carry), EditedTrackId));
    }

    /// <summary>During a live edit, moves the selected anchor from where it was when the edit began. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry)
    {
        if (liveEditStart is not { } start) return "No live edit is in progress.";
        if (SelectedAnchor is not { } kind) return "Select an anchor first.";
        if (UnplacedRefusal(kind) is { } unplaced) return unplaced;
        Scene = Moved(start.Scene, kind, world, carry);
        return null;
    }

    /// <summary>Moves a placed scene anchor to the camera's X and Z at foot height, keeping its yaw and carrying every track. Returns why it was refused, or null.</summary>
    public string? BringScene(Vector3 camera)
    {
        if (UnplacedRefusal(AnchorKind.Scene) is { } unplaced) return unplaced;
        var height = footHeight() ?? Scene.Anchor.Position.Y;
        return CommitScene(scene => (SceneGeometry.MoveSceneAnchor(scene, scene.Anchor with { Position = new Vector3(camera.X, height, camera.Z) }, carry: true), EditedTrackId));
    }

    private Scene Moved(Scene scene, AnchorKind kind, Anchor world, bool carry)
        => kind == AnchorKind.Scene
            ? SceneGeometry.MoveSceneAnchor(scene, world, carry)
            : SceneGeometry.MoveTrackAnchor(scene, EditedTrackId, world, carry);

    private EditSnapshot Current => new(Scene, EditedTrackId, Selected);

    private string? Apply(Func<Track, Track> change, Func<Track, int?> selectAfter) => ApplyScene(ChangeEdited(change), selectAfter);

    /// <summary>Applies a scene change to the edited track's points, keeping the timing selection in step.</summary>
    private string? ApplyScene(Func<Scene, Scene> change, Func<Track, int?> selectAfter)
    {
        var pointsBefore = Local.Points;
        var refusal = CommitEdit(change, selectAfter);
        if (refusal is null) RefreshTimingSelection(pointsBefore);
        return refusal;
    }

    /// <summary>Applies a timing change, keeping the point selection and any timing selection still in range.</summary>
    private string? ApplyTiming(Func<Track, Track> change)
    {
        var refusal = CommitEdit(ChangeEdited(change), _ => Selected);
        if (refusal is not null) return refusal;
        if (SelectedKey is { } key && key >= TrackEditing.KeyCount(Local)) SelectedKey = null;
        if (SelectedLeg is { } leg && leg >= Local.Points.Count) SelectedLeg = null;
        return null;
    }

    /// <summary>A scene change that applies <paramref name="change"/> to the edited track, refusing one that swaps the track.</summary>
    private Func<Scene, Scene> ChangeEdited(Func<Track, Track> change)
        => scene =>
        {
            var before = SceneEditing.Get(scene, EditedTrackId);
            var result = change(before);
            if (ReferenceEquals(result, before)) return scene;
            if (result.Id != EditedTrackId) throw new ArgumentException("A change cannot replace the track.");
            return SceneEditing.Replace(scene, result);
        };

    /// <summary>Applies a change to the scene as one undo step if the edited track can still be played. Returns why it was refused, or null.</summary>
    private string? CommitEdit(Func<Scene, Scene> change, Func<Track, int?> selectAfter)
    {
        StopPreview();
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";
        EndLiveEdit();

        try
        {
            var result = change(Scene);
            if (ReferenceEquals(result, Scene)) return null;

            var edited = SceneEditing.Get(result, EditedTrackId);
            _ = new TrackEvaluator(edited);
            history.Record(Current);
            var selected = selectAfter(edited);
            Scene = result;
            Selected = selected;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Applies a scene change and the edited track it leaves, as one undo step. Returns why it was refused, or null.</summary>
    private string? CommitScene(Func<Scene, (Scene Scene, Guid Edited)> change)
    {
        StopPreview();
        if (Mode != CameraMode.Editing) return "The scene can only change while editing.";
        EndLiveEdit();

        try
        {
            var (result, edited) = change(Scene);
            if (edited == EditedTrackId && (ReferenceEquals(result, Scene) || SameValues(result, Scene))) return null;

            history.Record(Current);
            if (edited != EditedTrackId) ClearForSwitch();
            Scene = result;
            EditedTrackId = edited;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Clears the point, key and leg selection and puts the scrub head at 0, as switching tracks does.</summary>
    private void ClearForSwitch()
    {
        Selected = null;
        SelectedKey = null;
        SelectedLeg = null;
        SelectedAnchor = null;
        scrubTime = 0.0;
    }

    private bool Restore(EditSnapshot? snapshot)
    {
        if (snapshot is not { } s) return false;
        var pointsBefore = Local.Points;
        if (s.Edited != EditedTrackId) ClearForSwitch();
        Scene = s.Scene;
        EditedTrackId = s.Edited;
        Selected = s.Selected;
        if (Selected is not null || (SelectedAnchor is { } kind && UnplacedRefusal(kind) is not null)) SelectedAnchor = null;
        RefreshTimingSelection(pointsBefore);
        return true;
    }

    /// <summary>Points the timing selection at the selected point's key, or clears a point key's selection when no point is selected.</summary>
    private void SyncKeyToPoint()
    {
        if (Selected is { } point)
        {
            SelectedKey = TrackEditing.PointKey(Local, point);
            SelectedLeg = null;
        }
        else if (SelectedKey is { } key && (key >= TrackEditing.KeyCount(Local) || TrackEditing.RoleOf(Local, key) == KeyRole.Point))
        {
            SelectedKey = null;
        }
    }

    /// <summary>After a point edit: a point key follows its point, and any other timing selection clears unless it's a leg and the points are unchanged.</summary>
    private void RefreshTimingSelection(IReadOnlyList<ControlPoint> pointsBefore)
    {
        if (!ReferenceEquals(pointsBefore, Local.Points) && !pointsBefore.SequenceEqual(Local.Points)) SelectedLeg = null;
        if (SelectedKey is { } key && (Selected is null || key >= TrackEditing.KeyCount(Local) || TrackEditing.RoleOf(Local, key) != KeyRole.Point)) SelectedKey = null;
        if (Selected is not null && SelectedLeg is null) SelectedKey = TrackEditing.PointKey(Local, Selected.Value);
    }

    /// <summary>Sets Manual slopes from one graph slope on the handled sides: <paramref name="only"/> alone, or both when null.</summary>
    private static Track Collinear(Track track, TrackEvaluator evaluator, int key, float distancePerSecond, KeySide? only)
    {
        float? Side(KeySide side) => (only is null || only == side) && TimingEditing.HasHandle(track, key, side)
            ? evaluator.ToStoredSlope(key, side, distancePerSecond)
            : null;
        return TimingEditing.SetHandles(track, key, Side(KeySide.In), Side(KeySide.Out));
    }

    /// <summary>Replaces the track with <paramref name="change"/> of the live edit's starting track. Returns why it was refused, or null.</summary>
    private string? PreviewFromStart(Func<Track, TrackEvaluator, Track> change)
    {
        if (liveEditStart is not { } start) return "No live edit is in progress.";
        var startTrack = SceneEditing.Get(start.Scene, start.Edited);
        if (!ReferenceEquals(evaluatedStart, startTrack))
        {
            liveStartEvaluator = new TrackEvaluator(startTrack);
            evaluatedStart = startTrack;
        }

        try
        {
            var result = change(startTrack, liveStartEvaluator!);
            _ = new TrackEvaluator(result);
            Local = result;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private string? SelectionRefusal()
        => Mode != CameraMode.Editing ? "The track can only change while editing."
         : Selected is null ? "Select a point first." : null;

    private static int Follow(int selected, int from, int to)
    {
        if (selected == from) return to;
        if (from < selected && selected <= to) return selected - 1;
        if (to <= selected && selected < from) return selected + 1;
        return selected;
    }

    /// <summary>The evaluator for the current track, rebuilt when the track changes.</summary>
    public TrackEvaluator Evaluator
    {
        get
        {
            if (!ReferenceEquals(evaluatedTrack, Track))
            {
                evaluator = new TrackEvaluator(Track);
                evaluatedTrack = Track;
            }

            return evaluator!;
        }
    }

    /// <summary>The track's frame at <paramref name="time"/> seconds, or null with no points.</summary>
    public CameraState? FrameAt(double time) => Local.Points.Count == 0 ? null : Evaluator.Evaluate(time);

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Seconds under the scrub head: shot time while live or previewing, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => Mode == CameraMode.Live ? Director.ShotTime : preview?.ShotTime ?? Math.Min(scrubTime, Duration);

    /// <summary>Starts dragging the scrub head; live, playback holds until <see cref="EndScrub"/>. No effect when off.</summary>
    public void BeginScrub()
    {
        StopPreview();
        if (Mode == CameraMode.Off || Scrubbing) return;
        Scrubbing = true;
        resumeAfterScrub = Mode == CameraMode.Live && !Director.IsPaused;
        if (Mode == CameraMode.Live) Director.Pause();
    }

    /// <summary>Moves the scrub head to <paramref name="time"/> within the track; live, playback seeks there. No effect when off.</summary>
    public void ScrubTo(double time)
    {
        if (Mode == CameraMode.Editing) StopPreview();
        if (Mode == CameraMode.Off) return;
        scrubTime = Math.Clamp(time, 0.0, Duration);
        if (Mode == CameraMode.Live) Director.Seek(scrubTime);
    }

    /// <summary>Stops dragging the scrub head; live, playback carries on as it was before.</summary>
    public void EndScrub()
    {
        if (!Scrubbing) return;
        Scrubbing = false;
        if (Mode == CameraMode.Live && resumeAfterScrub) Director.Resume();
    }
}
