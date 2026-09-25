using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Session;

/// <summary>The mode, the Director, the scene and the edited track: the rules for moving between modes, and every edit as one undo step.</summary>
public sealed class SessionState
{
    private readonly EditHistory history = new();
    private readonly Func<Vector3, float?> groundBelow;
    private readonly NearbyCharacters? aimTargets;
    private readonly EvaluatorCache liveStartEvaluator = new();
    private EditSnapshot? liveEditStart;

    /// <summary>A session; <paramref name="groundBelow"/> finds the ground's height under a world point, or null when it can't, and <paramref name="aimTargets"/> finds watched or followed characters.</summary>
    public SessionState(Func<Vector3, float?>? groundBelow = null, NearbyCharacters? aimTargets = null)
    {
        this.groundBelow = groundBelow ?? (_ => null);
        this.aimTargets = aimTargets;
        Director = new Director(aimTargets);
        Selection = new SelectionState(this);
        World = new WorldView(this, aimTargets);
        Transport = new Transport(this);
        EditedTrackId = Scene.Tracks[0].Id;
    }

    public CameraMode Mode { get; private set; }

    public Director Director { get; }

    /// <summary>What's selected while editing.</summary>
    public SelectionState Selection { get; }

    /// <summary>The scene's tracks as they stand in the world.</summary>
    public WorldView World { get; }

    /// <summary>The Edit preview and the scrub head.</summary>
    public Transport Transport { get; }

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

    /// <summary>The edited track as stored, local to its anchor; a new instance only when it is edited.</summary>
    public Track StoredTrack => Local;

    /// <summary>The edited track in the world as the editor shows it; Edit builds it.</summary>
    public Track Track => World.Shown(Local);

    /// <summary>The track's length in seconds: its last compiled key, or 0 with no points.</summary>
    public double Duration => World.Evaluator.Duration;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => !Released;

    /// <summary>True in Off and View, where the game has its camera.</summary>
    public bool Released => Mode is CameraMode.Off or CameraMode.View;

    /// <summary>True while a preview is running in Edit, or a live shot is running and neither paused nor finished.</summary>
    public bool IsPlaying =>
        Transport.Previewing || (Mode == CameraMode.Live && !Director.IsPaused && !Director.IsFinished);

    /// <summary>True while editing with a step to undo.</summary>
    public bool CanUndo => Mode == CameraMode.Editing && history.CanUndo;

    /// <summary>True while editing with a step to redo.</summary>
    public bool CanRedo => Mode == CameraMode.Editing && history.CanRedo;

    /// <summary>True when Vista hasn't stopped and the playlist has an entry whose track has points.</summary>
    public bool CanGoLive => !Stopped && PlaylistEditing.CanPlay(Scene);

    /// <summary>Why a preview is refused outside a live edit.</summary>
    private const string NoLiveEdit = "No live edit is in progress.";

    /// <summary>Why a track edit is refused outside Edit.</summary>
    private const string TrackOnlyWhileEditing = "The track can only change while editing.";

    /// <summary>Why an edit of the selected point is refused with none selected.</summary>
    private const string SelectAPoint = "Select a point first.";

    /// <summary>Why an edit of points is refused when one isn't in the track.</summary>
    private const string NoSuchPoint = "There is no such point.";

    /// <summary>What the player is told once Vista has stopped, and why Edit and Live are refused.</summary>
    public const string StopMessage =
        "Vista has stopped. Reload it in /xlplugins, or check for an update if that doesn't help.";

    /// <summary>True once a fault or a failed touch point has stopped Vista; only reloading the plugin clears it.</summary>
    public bool Stopped => StopReason is not null;

    /// <summary>Why Vista first stopped, for the log; null until it stops.</summary>
    public string? StopReason { get; private set; }

    /// <summary>Stops Vista after a fault at <paramref name="where"/>. True when this is the first stop, so the player is told.</summary>
    public bool ReportFault(string where) => Halt(FaultReason(where));

    /// <summary>The stop reason for a fault at <paramref name="where"/>.</summary>
    public static string FaultReason(string where) => $"fault in {where}";

    /// <summary>Takes a touch point's startup check; a failed one stops Vista. True when this is the first stop, so the player is told.</summary>
    public bool ReportTouchPoint(string name, bool passed) => !passed && Halt($"{name} unavailable");

    /// <summary>Releases to Off and refuses Edit and Live from now on, keeping the first reason.</summary>
    private bool Halt(string reason)
    {
        Release();
        if (Stopped)
            return false;
        StopReason = reason;
        return true;
    }

    /// <summary>True when Play has something to play: the edited track's points in Edit, otherwise a playlist that can go live.</summary>
    public bool CanStart => Mode == CameraMode.Editing ? Local.Points.Count > 0 : CanGoLive;

    /// <summary>True when Restart has something to play, which Off and View never do.</summary>
    public bool CanRestart => !Released && CanStart;

    /// <summary>True in Edit while no preview plays: the overlay takes clicks and shows the gizmo.</summary>
    public bool OverlayEditable => Mode == CameraMode.Editing && !Transport.Previewing;

    /// <summary>True when the tracks are drawn over the game: in View, and in Edit while no preview plays.</summary>
    public bool OverlayShown => Mode == CameraMode.View || OverlayEditable;

    /// <summary>Why a point can't be taken from the camera now, or null.</summary>
    public string? AddPointRefusal =>
        Mode != CameraMode.Editing ? "Points can only be added while editing."
        : Transport.Scrubbing ? "Points cannot be added while scrubbing."
        : null;

    /// <summary>The entry playing while live, or null.</summary>
    public PlaylistEntry? PlayingEntry =>
        Mode == CameraMode.Live && Director.Playlist is { } playing
            ? Scene.Playlist.FirstOrDefault(e => e.Id == playing.EntryId)
            : null;

    /// <summary>The edited Follow Target track's offset as an orbit round its character, or null unless it follows with its one point.</summary>
    public Orbit? FollowOrbit =>
        Local is { Aim: AimMode.FollowTarget, Points.Count: 1 } local
            ? Tracks.Aiming.FollowOrbit.Of(local.Points[0])
            : null;

    /// <summary>Enters editing; from live, takes the Director offline. Refused once Vista has stopped.</summary>
    public EditOutcome Edit()
    {
        if (Stopped)
            return EditOutcome.Refused;
        Transport.StopPreview();
        switch (Mode)
        {
            case CameraMode.Editing:
                return EditOutcome.Unchanged;
            case CameraMode.Live:
                Transport.DropScrub();
                Transport.Park(
                    PlayingEntry?.TrackId == EditedTrackId ? Math.Clamp(Director.ShotTime, 0.0, Duration) : 0.0
                );
                Director.GoOffline();
                Mode = CameraMode.Editing;
                return EditOutcome.FromLive;
            default:
                Transport.DropScrub();
                Mode = CameraMode.Editing;
                return EditOutcome.FromGame;
        }
    }

    /// <summary>In Edit, previews from the scrub head; live, resumes a paused shot or leaves a playing one alone; otherwise goes live with the playlist.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Editing)
            return Transport.Previewing ? PlayOutcome.Previewed : StartPreview(fromStart: false);
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused)
                return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return GoLive();
    }

    /// <summary>In Edit, previews from the beginning; otherwise goes live with the playlist from the start. Refused when nothing can play or Vista has stopped.</summary>
    public PlayOutcome Restart() => Mode == CameraMode.Editing ? StartPreview(fromStart: true) : GoLive();

    /// <summary>Goes live with the playlist paused at its start. Refused when nothing can play or Vista has stopped.</summary>
    public PlayOutcome Cue()
    {
        var outcome = GoLive();
        if (outcome == PlayOutcome.Refused)
            return outcome;
        Director.Pause();
        return outcome == PlayOutcome.StartedFromGame ? PlayOutcome.CuedFromGame : PlayOutcome.Cued;
    }

    /// <summary>Live, holds the current frame; in Edit, stops a preview. Returns false when there was nothing to stop.</summary>
    public bool Stop()
    {
        if (Mode == CameraMode.Editing)
            return Transport.StopPreview();
        if (Mode != CameraMode.Live)
            return false;
        Director.Pause();
        return true;
    }

    /// <summary>Hands the camera to the game in <paramref name="to"/>, Off or View, taking the Director offline. Returns false if the game already had it.</summary>
    public bool Release(CameraMode to = CameraMode.Off)
    {
        if (to is not (CameraMode.Off or CameraMode.View))
            throw new ArgumentOutOfRangeException(nameof(to), to, "Release goes to Off or View.");
        Transport.StopPreview();
        var owned = !Released;
        if (owned)
        {
            Transport.DropScrub();
            EndLiveEdit();
            Director.GoOffline();
        }

        Selection.DropGroup();
        Mode = to;
        return owned;
    }

    /// <summary>Goes live with the playlist from its start. Refused when nothing can play or Vista has stopped.</summary>
    private PlayOutcome GoLive()
    {
        if (Stopped)
            return PlayOutcome.Refused;
        var items = PlaylistItems();
        if (items.Count == 0)
            return PlayOutcome.Refused;
        Transport.StopPreview();
        Transport.DropScrub();
        EndLiveEdit();

        Selection.DropGroup();
        Director.GoLive(new PlaylistShot(items, Scene.PlaylistLoops));
        var fromGame = Released;
        Mode = CameraMode.Live;
        return fromGame ? PlayOutcome.StartedFromGame : PlayOutcome.Started;
    }

    /// <summary>The playlist's entries whose tracks have points, in order and in the world, as Live plays them.</summary>
    public IReadOnlyList<PlaylistItem> PlaylistItems() =>
        Scene
            .Playlist.Select(entry => (Entry: entry, Track: SceneEditing.Get(Scene, entry.TrackId)))
            .Where(x => x.Track.Points.Count > 0)
            .Select(x => new PlaylistItem(x.Entry.Id, World.WorldOf(x.Track), x.Entry.Loops))
            .ToList();

    /// <summary>Starts an Edit preview from the scrub head, or from the beginning when asked or when the scrub head is where the shot finishes.</summary>
    private PlayOutcome StartPreview(bool fromStart)
    {
        if (!CanStart)
            return PlayOutcome.Refused;
        EndLiveEdit();
        Transport.DropScrub();
        Transport.StartPreview(new TrackPlayback(World.WorldOf(Local), aimTargets), fromStart);
        return PlayOutcome.Previewed;
    }

    /// <summary>Opens <paramref name="scene"/> editing its first track, shown if hidden, clearing the selection, scrub head and undo history; the mode stays. Returns why it was refused, or null.</summary>
    public string? LoadScene(Scene scene)
    {
        if (Mode == CameraMode.Live)
            return "A scene can't be loaded while Live.";
        Transport.StopPreview();
        liveEditStart = null;
        var first = scene.Tracks[0].Id;
        Scene = SceneEditing.SetHidden(scene, [first], false);
        EditedTrackId = first;
        ClearForSwitch();
        World.Clear();
        history.Clear();
        return null;
    }

    /// <summary>Adds <paramref name="preset"/> as a new track on the ground under <paramref name="camera"/>, or at its height with no ground, and edits it. Returns why it was refused, or null.</summary>
    public string? AddPreset(Preset preset, Vector3 camera) =>
        CommitScene(scene => Presets.Place(scene, preset, camera with { Y = groundBelow(camera) ?? camera.Y }));

    /// <summary>Edits track <paramref name="id"/>, showing it first if hidden. Not an undo step itself. Returns why it was refused, or null.</summary>
    public string? SwitchTrack(Guid id)
    {
        if (Mode != CameraMode.Editing)
            return "Tracks can only be switched while editing.";
        if (!SceneEditing.TryGet(Scene, id, out _))
            return SceneEditing.NoSuchTrack;
        if (Scene.Hidden.Contains(id) && SetTracksHidden([id], false) is { } refusal)
            return refusal;
        if (id == EditedTrackId)
            return null;

        Transport.StopPreview();
        EndLiveEdit();
        ClearForSwitch();
        EditedTrackId = id;
        return null;
    }

    /// <summary>Edits track <paramref name="track"/> and selects its point <paramref name="index"/>. Returns why it was refused, or null.</summary>
    public string? SelectPoint(Guid track, int index)
    {
        var refusal = SwitchTrack(track);
        if (refusal is null)
            Selection.Select(index);
        return refusal;
    }

    /// <summary>Adds an empty track at the end and edits it. Returns why it was refused, or null.</summary>
    public string? AddTrack() => CommitScene(scene => SceneEditing.Add(scene));

    /// <summary>Renames track <paramref name="id"/>. Returns why it was refused, or null.</summary>
    public string? RenameTrack(Guid id, string name) =>
        CommitScene(scene => (SceneEditing.Rename(scene, id, name), EditedTrackId));

    /// <summary>Copies track <paramref name="id"/> after itself and edits the copy. Returns why it was refused, or null.</summary>
    public string? DuplicateTrack(Guid id) => CommitScene(scene => SceneEditing.Duplicate(scene, id));

    /// <summary>Deletes tracks <paramref name="ids"/>; deleting the edited track edits the first remaining track after it, or the last, shown if it was hidden. Returns why it was refused, or null.</summary>
    public string? DeleteTracks(IReadOnlyCollection<Guid> ids) =>
        CommitScene(scene =>
        {
            var (result, next) = SceneEditing.Delete(scene, ids, EditedTrackId);
            if (result.Hidden.Contains(next))
                result = SceneEditing.SetHidden(result, [next], false);
            return (result, next);
        });

    /// <summary>Moves tracks <paramref name="ids"/>, grabbed by <paramref name="grabbed"/>, as a block onto <paramref name="target"/>, or the end when null. Returns why it was refused, or null.</summary>
    public string? MoveTracks(IReadOnlyCollection<Guid> ids, Guid grabbed, Guid? target) =>
        CommitScene(scene =>
        {
            var order = BlockMove.Order(
                scene.Tracks.Count,
                ids.Select(id => SceneEditing.Require(scene, id)).ToArray(),
                SceneEditing.Require(scene, grabbed),
                target is { } t ? SceneEditing.Require(scene, t) : null
            );
            return (order is null ? scene : SceneEditing.Reorder(scene, order), EditedTrackId);
        });

    /// <summary>Hides or shows tracks <paramref name="ids"/>; hiding skips the edited track, which is always shown. Returns why it was refused, or null.</summary>
    public string? SetTracksHidden(IReadOnlyCollection<Guid> ids, bool hidden)
    {
        var change = hidden ? ids.Where(id => id != EditedTrackId).ToArray() : ids;
        if (hidden && ids.Count > 0 && change.Count == 0)
            return "The track being edited is always shown.";
        return CommitScene(scene => (SceneEditing.SetHidden(scene, change, hidden), EditedTrackId));
    }

    /// <summary>Adds an entry for each of tracks <paramref name="ids"/>, in Hierarchy order, at <paramref name="index"/>, or at the end. Returns why it was refused, or null.</summary>
    public string? AddToPlaylist(IReadOnlyCollection<Guid> ids, int? index = null) =>
        CommitScene(scene =>
            (
                PlaylistEditing.Add(scene, ids.OrderBy(id => SceneEditing.IndexOf(scene, id)).ToArray(), index),
                EditedTrackId
            )
        );

    /// <summary>Removes playlist entries <paramref name="ids"/>. Returns why it was refused, or null.</summary>
    public string? RemoveFromPlaylist(IReadOnlyCollection<Guid> ids) =>
        CommitScene(scene => (PlaylistEditing.Remove(scene, ids), EditedTrackId));

    /// <summary>Moves playlist entries <paramref name="ids"/>, grabbed by <paramref name="grabbed"/>, as a block onto <paramref name="target"/>, or the end when null. Returns why it was refused, or null.</summary>
    public string? MoveEntries(IReadOnlyCollection<Guid> ids, Guid grabbed, Guid? target) =>
        CommitScene(scene =>
        {
            var order = BlockMove.Order(
                scene.Playlist.Count,
                ids.Select(id => PlaylistEditing.Require(scene, id)).ToArray(),
                PlaylistEditing.Require(scene, grabbed),
                target is { } t ? PlaylistEditing.Require(scene, t) : null
            );
            return (order is null ? scene : PlaylistEditing.Reorder(scene, order), EditedTrackId);
        });

    /// <summary>Sets how many times an entry plays, or null to follow its track. Returns why it was refused, or null.</summary>
    public string? SetEntryLoops(Guid entryId, int? loops) =>
        CommitScene(scene => (PlaylistEditing.SetLoops(scene, entryId, loops), EditedTrackId));

    /// <summary>Sets whether Live loops the playlist, as one undo step. Returns why it was refused, or null.</summary>
    public string? SetPlaylistLoops(bool loops) =>
        CommitScene(scene => (PlaylistEditing.SetPlaylistLoops(scene, loops), EditedTrackId));

    /// <summary>Applies <paramref name="change"/> if editing and the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change) =>
        Apply(change, result => Selection.Points.Where(p => TrackEditing.IsPoint(result, p)).ToArray());

    /// <summary>Appends a world point, placing the anchors under a first point; the selection is unchanged.</summary>
    public string? AddToEnd(ControlPoint point) =>
        ApplyScene(scene => WithPoint(scene, point, TrackEditing.Append), _ => Selection.Points);

    /// <summary>Inserts a world point after the selected one and selects it.</summary>
    public string? AddAfterSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal)
            return refusal;
        var s = Selection.Point!.Value;
        var added = ApplyScene(
            scene => WithPoint(scene, point, (t, p) => TrackEditing.InsertAfter(t, s, p)),
            _ => [s + 1]
        );
        if (added is null)
            Selection.LastPoint = s + 1;
        return added;
    }

    /// <summary>Replaces the selected point, keeping its timing and the selection.</summary>
    public string? OverwriteSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal)
            return refusal;
        return ReplacePoint(Selection.Point!.Value, point);
    }

    /// <summary>Replaces point <paramref name="index"/> with a world point, keeping its timing and the selection.</summary>
    public string? ReplacePoint(int index, ControlPoint point) =>
        Apply(t => TrackEditing.Replace(t, index, ToLocal(point)), _ => Selection.Points);

    /// <summary>Deletes the selected points.</summary>
    public string? DeleteSelected()
    {
        if (Mode != CameraMode.Editing)
            return TrackOnlyWhileEditing;
        return Selection.Points.Count == 0 ? SelectAPoint : DeletePoints(Selection.Points);
    }

    /// <summary>Deletes points <paramref name="indices"/>; any other selected point stays selected.</summary>
    public string? DeletePoints(IReadOnlyCollection<int> indices)
    {
        if (indices.Count == 0 || !indices.All(i => TrackEditing.IsPoint(Local, i)))
            return NoSuchPoint;
        int? Kept(int p) => indices.Contains(p) ? null : p - indices.Distinct().Count(d => d < p);
        var kept = Selection.Points.Select(Kept).OfType<int>().ToArray();
        var last = Selection.LastPoint is { } l ? Kept(l) : null;
        var refusal = Apply(t => TrackEditing.Delete(t, indices), _ => kept);
        if (refusal is null)
            Selection.LastPoint = last;
        return refusal;
    }

    /// <summary>Moves points <paramref name="indices"/> to the end of track <paramref name="destination"/>, or a new track when null, keeping their places in the world, then edits it with them selected. Returns why it was refused, or null.</summary>
    public string? MovePointsTo(IReadOnlyCollection<int> indices, Guid? destination)
    {
        if (Mode != CameraMode.Editing)
            return TrackOnlyWhileEditing;
        if (indices.Count == 0 || !indices.All(i => TrackEditing.IsPoint(Local, i)))
            return NoSuchPoint;

        var points = indices.Distinct().Order().ToArray();
        var world = Track;
        IReadOnlyList<int> moved = [];

        // The undo step records these points as selected, so undoing the move selects them even when they weren't.
        var before = Selection.Value;
        Selection.Value = new SelectedItems(points, [], []);
        var refusal = CommitScene(scene =>
        {
            var (result, to, landed) = PointTransfer.Move(
                scene,
                EditedTrackId,
                points,
                points.Select(i => world.Points[i]).ToArray(),
                destination,
                groundBelow
            );
            moved = landed;
            return (SceneEditing.SetHidden(result, [to], false), to);
        });
        if (refusal is null)
            Selection.SelectPoints(moved);
        else
            Selection.Value = before;
        return refusal;
    }

    /// <summary>Moves points <paramref name="points"/>, grabbed by <paramref name="grabbed"/>, as a block onto <paramref name="target"/>, or the end when null; the selection stays on the same points.</summary>
    public string? MovePoints(IReadOnlyCollection<int> points, int grabbed, int? target)
    {
        int[]? order = null;
        if (Refusal(() => order = BlockMove.Order(Local.Points.Count, points, grabbed, target)) is { } refused)
            return refused;
        if (order is null)
            return null;

        var moved = Selection.Points.Select(p => BlockMove.NewIndex(order, p)).Order().ToArray();
        var last = Selection.LastPoint is { } l && l < order.Length ? BlockMove.NewIndex(order, l) : (int?)null;
        var refusal = Apply(t => TrackEditing.Reorder(t, order), _ => moved);
        if (refusal is null)
            Selection.LastPoint = last;
        return refusal;
    }

    /// <summary>A world point as the edited track stores it: relative to its character for a Follow track, otherwise to its anchor.</summary>
    private ControlPoint ToLocal(ControlPoint world) =>
        World.FollowFrame(Local) is { } frame
            ? frame.ToLocal(world)
            : SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world);

    /// <summary>Places the anchors under a first point if needed, then adds the world point to the edited track with <paramref name="add"/>.</summary>
    private Scene WithPoint(Scene scene, ControlPoint world, Func<Track, ControlPoint, Track> add)
    {
        var local = SceneEditing.Get(scene, EditedTrackId);
        if (local.Aim == AimMode.FollowTarget)
        {
            if (local.Points.Count >= 1)
                throw new ArgumentException(TrackEditing.FollowHasOnePoint);
            if (local.TargetName is null)
                throw new ArgumentException("Choose a character to follow.");
            if (World.FollowFrame(scene, local) is null)
                throw new ArgumentException("Character not found.");
        }

        var placed =
            scene.AnchorPlaced && SceneEditing.Get(scene, EditedTrackId).AnchorPlaced
                ? scene
                : SceneGeometry.PlaceFor(
                    scene,
                    EditedTrackId,
                    world.Position,
                    groundBelow(world.Position) ?? world.Position.Y
                );
        var track = SceneEditing.Get(placed, EditedTrackId);
        var stored = World.FollowFrame(placed, track) is { } frame
            ? frame.ToLocal(world)
            : SceneGeometry.WorldAnchor(placed, track).ToLocal(world);
        return SceneEditing.Replace(placed, add(track, stored));
    }

    private string? SelectionRefusal() =>
        Mode != CameraMode.Editing ? TrackOnlyWhileEditing
        : Selection.Points.Count == 0 ? SelectAPoint
        : Selection.Points.Count > 1 ? "Select one point first."
        : null;

    /// <summary>Sets the track's speed. Returns why it was refused, or null.</summary>
    public string? SetTrackSpeed(float speed) => ApplyTiming(t => TrackEditing.SetSpeed(t, speed));

    /// <summary>Sets the track's speed so the shot takes about <paramref name="seconds"/>. Returns why it was refused, or null.</summary>
    public string? SetTrackDuration(float seconds) => ApplyTiming(t => TrackEditing.SetDuration(t, seconds));

    /// <summary>Pins leg <paramref name="leg"/> at the speed that takes <paramref name="seconds"/>. Returns why it was refused, or null.</summary>
    public string? SetLegDuration(int leg, float seconds) =>
        ApplyTiming(t => TrackEditing.SetLegDuration(t, leg, seconds));

    /// <summary>Pins leg <paramref name="leg"/> at <paramref name="speed"/>. Returns why it was refused, or null.</summary>
    public string? SetLegSpeed(int leg, float speed) => ApplyTiming(t => TrackEditing.SetLegSpeed(t, leg, speed));

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
        if (refusal is null)
            Selection.Key = null;
        return refusal;
    }

    /// <summary>Lets key <paramref name="key"/>'s handles move separately. Returns why it was refused, or null.</summary>
    public string? BreakHandles(int key) => ApplyTiming(t => TimingEditing.SetBroken(t, key, true));

    /// <summary>Joins key <paramref name="key"/>'s handles at the slope the <paramref name="from"/> side has in the graph. Returns why it was refused, or null.</summary>
    public string? UnifyHandles(int key, KeySide from) =>
        ApplyTiming(t =>
        {
            var joined = TimingEditing.SetBroken(t, key, false);
            return Collinear(joined, World.Evaluator, key, World.Evaluator.SideSlope(key, from), null);
        });

    /// <summary>Sets Manual slopes from one graph slope on the handled sides: <paramref name="only"/> alone, or both when null.</summary>
    private static Track Collinear(
        Track track,
        TrackEvaluator evaluator,
        int key,
        float distancePerSecond,
        KeySide? only
    )
    {
        float? Side(KeySide side) =>
            (only is null || only == side) && TimingEditing.HasHandle(track, key, side)
                ? evaluator.ToStoredSlope(key, side, distancePerSecond)
                : null;
        return TimingEditing.SetHandles(track, key, Side(KeySide.In), Side(KeySide.Out));
    }

    /// <summary>Sets the aim mode, keeping a one-point track's point where it is shown; the first Look At places its point from the first point, or from the world <paramref name="camera"/> with no points. Returns why it was refused, or null.</summary>
    public string? SetAim(AimMode aim, ControlPoint camera)
    {
        var anchor = SceneGeometry.WorldAnchor(Scene, Local);
        var local = anchor.ToLocal(camera);
        var shown = ShownPoint;
        return ApplySetting(t =>
        {
            if (TrackEditing.AimRefusal(t, aim) is { } refusal)
                throw new ArgumentException(refusal);
            if (t.Aim == aim)
                return t;
            var leaving =
                t.Aim == AimMode.FollowTarget && shown is { } s ? TrackEditing.Replace(t, 0, anchor.ToLocal(s)) : t;
            var result = TrackEditing.SetAim(leaving, aim, local);
            return shown is { } p && World.FollowFrame(Scene, result) is { } frame
                ? TrackEditing.Replace(result, 0, frame.ToLocal(p))
                : result;
        });
    }

    /// <summary>Names the character to watch or follow by name and home world, or none; a Follow point stays where it is shown for a first character and keeps its orbit for a new one. Returns why it was refused, or null.</summary>
    public string? SetTarget(string? name, string? world)
    {
        var shown = ShownPoint;
        return ApplySetting(t =>
        {
            var result = TrackEditing.SetTarget(t, name, world);
            // A first character takes the camera where it is; a new one keeps the orbit, so the shot moves with the choice.
            return
                !ReferenceEquals(result, t)
                && t.TargetName is null
                && shown is { } s
                && World.FollowFrame(Scene, result) is { } frame
                ? TrackEditing.Replace(result, 0, frame.ToLocal(s))
                : result;
        });
    }

    /// <summary>The edited track's only point where it is shown, or null unless it has exactly one.</summary>
    private ControlPoint? ShownPoint => Local.Points.Count == 1 ? Track.Points[0] : null;

    /// <summary>Sets how far ahead along the path Direction of travel looks, in seconds. Returns why it was refused, or null.</summary>
    public string? SetLookAhead(float seconds) => ApplySetting(t => TrackEditing.SetLookAhead(t, seconds));

    /// <summary>Sets how heavily the aim eases onto the character. Returns why it was refused, or null.</summary>
    public string? SetSmoothing(float smoothing) => ApplySetting(t => TrackEditing.SetSmoothing(t, smoothing));

    /// <summary>Sets whether a Follow Target offset turns as the character turns. Returns why it was refused, or null.</summary>
    public string? SetFollowTurns(bool turns) => ApplySetting(t => TrackEditing.SetFollowTurns(t, turns));

    /// <summary>Sets whether a Follow Target camera looks at the character. Returns why it was refused, or null.</summary>
    public string? SetFollowLooks(bool looks) => ApplySetting(t => TrackEditing.SetFollowLooks(t, looks));

    /// <summary>True from <see cref="BeginLiveEdit"/> until the live edit ends, which an undo or leaving Edit also does.</summary>
    public bool LiveEditing => liveEditStart is not null;

    /// <summary>Starts a live edit: previews change the track at once and end as one undo step. Editing only.</summary>
    public void BeginLiveEdit()
    {
        Transport.StopPreview();
        if (Mode == CameraMode.Editing && liveEditStart is null)
            liveEditStart = Current;
    }

    /// <summary>Replaces point <paramref name="index"/> during a live edit without recording a step. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point)
    {
        if (liveEditStart is null)
            return NoLiveEdit;
        return Refusal(() =>
        {
            var result = TrackEditing.Replace(Local, index, ToLocal(point));
            _ = new TrackEvaluator(result);
            Local = result;
        });
    }

    /// <summary>During a live edit, sets the aim height without recording a step. Returns why it was refused, or null.</summary>
    public string? PreviewAimHeight(float yalms)
    {
        if (liveEditStart is null)
            return NoLiveEdit;
        Local = TrackEditing.SetAimHeight(Local, yalms);
        return null;
    }

    /// <summary>During a live edit, moves the Follow Target point to <paramref name="orbit"/> without recording a step. Returns why it was refused, or null.</summary>
    public string? PreviewFollowOrbit(Orbit orbit)
    {
        if (liveEditStart is null)
            return NoLiveEdit;
        if (Local is not { Aim: AimMode.FollowTarget, Points.Count: 1 } local)
            return "Only a Follow Target track with its point has an orbit.";
        Local = TrackEditing.Replace(local, 0, Tracks.Aiming.FollowOrbit.With(local.Points[0], orbit));
        return null;
    }

    /// <summary>During a live edit, drags key <paramref name="key"/> towards <paramref name="time"/> from the track as the edit began; <paramref name="ripple"/> carries every later key with it.</summary>
    public string? PreviewKeyMove(int key, float time, bool ripple = false) =>
        PreviewFromStart(
            (start, evaluator) =>
                ripple
                    ? TimingEditing.RippleKey(start, evaluator, key, time)
                    : TimingEditing.MoveKey(start, evaluator, key, time)
        );

    /// <summary>During a live edit, sets a handle to a slope in distance per second, both sides unless the key is broken.</summary>
    public string? PreviewHandle(int key, KeySide side, float distancePerSecond) =>
        PreviewFromStart(
            (start, evaluator) =>
                Collinear(
                    start,
                    evaluator,
                    key,
                    distancePerSecond,
                    start.Timing[TrackEditing.PointOf(start, key)].Broken ? side : null
                )
        );

    /// <summary>During a live edit, moves the selected anchor from where it was when the edit began. Returns why it was refused, or null.</summary>
    public string? PreviewAnchor(Anchor world, bool carry)
    {
        if (liveEditStart is not { } start)
            return NoLiveEdit;
        if (Selection.Anchor is not { } kind || kind == AnchorKind.LookAt)
            return "Select an anchor first.";
        if (Selection.UnplacedRefusal(kind) is { } unplaced)
            return unplaced;
        Scene =
            kind == AnchorKind.Scene
                ? SceneGeometry.MoveSceneAnchor(start.Scene, world, carry)
                : SceneGeometry.MoveTrackAnchor(start.Scene, EditedTrackId, world, carry);
        return null;
    }

    /// <summary>During a live edit, moves the selected Look At point to <paramref name="world"/>. Returns why it was refused, or null.</summary>
    public string? PreviewLookAt(Vector3 world)
    {
        if (liveEditStart is null)
            return NoLiveEdit;
        if (Selection.Anchor != AnchorKind.LookAt)
            return "Select the Look At point first.";
        if (Selection.UnplacedRefusal(AnchorKind.LookAt) is { } unused)
            return unused;
        Local = TrackEditing.SetLookAt(Local, SceneGeometry.WorldAnchor(Scene, Local).ToLocal(world));
        return null;
    }

    /// <summary>Ends a live edit, recording it as one undo step if the scene changed.</summary>
    public void EndLiveEdit()
    {
        if (liveEditStart is not { } start)
            return;
        liveEditStart = null;
        if (ReferenceEquals(start.Scene, Scene))
            return;

        // Previews rebuild the lists, so compare values: a drag back to the start is no step.
        if (SameValues(start.Scene, Scene))
            Scene = start.Scene;
        else
            history.Record(start);
    }

    /// <summary>Replaces the track with <paramref name="change"/> of the live edit's starting track. Returns why it was refused, or null.</summary>
    private string? PreviewFromStart(Func<Track, TrackEvaluator, Track> change)
    {
        if (liveEditStart is not { } start)
            return NoLiveEdit;
        var startTrack = SceneEditing.Get(start.Scene, start.Edited);
        var evaluator = liveStartEvaluator.For(startTrack);
        return Refusal(() =>
        {
            var result = change(startTrack, evaluator);
            _ = new TrackEvaluator(result);
            Local = result;
        });
    }

    /// <summary>Restores the scene, the edited track and the selection before the last change. Returns false if nothing was undone.</summary>
    public bool Undo()
    {
        Transport.StopPreview();
        EndLiveEdit();
        return Restore(Mode == CameraMode.Editing ? history.Undo(Current) : null);
    }

    /// <summary>Re-applies the last undone change. Returns false if nothing was redone.</summary>
    public bool Redo()
    {
        Transport.StopPreview();
        EndLiveEdit();
        return Restore(Mode == CameraMode.Editing ? history.Redo(Current) : null);
    }

    private EditSnapshot Current => new(Scene, EditedTrackId, Selection.Value);

    private string? Apply(Func<Track, Track> change, Func<Track, IReadOnlyList<int>> selectAfter) =>
        ApplyScene(ChangeEdited(change), selectAfter);

    /// <summary>Applies a scene change to the edited track's points, keeping the timing selection in step.</summary>
    private string? ApplyScene(Func<Scene, Scene> change, Func<Track, IReadOnlyList<int>> selectAfter)
    {
        var pointsBefore = Local.Points;
        var refusal = CommitEdit(change, selectAfter);
        if (refusal is null)
            Selection.RefreshTiming(pointsBefore);
        return refusal;
    }

    /// <summary>Applies a timing change, keeping the point selection and any timing selection still in range.</summary>
    private string? ApplyTiming(Func<Track, Track> change)
    {
        var refusal = CommitEdit(ChangeEdited(change), _ => Selection.Points);
        if (refusal is not null)
            return refusal;
        Selection.AfterTiming();
        return null;
    }

    /// <summary>Applies a change to the edited track's settings as one undo step, keeping the selection.</summary>
    private string? ApplySetting(Func<Track, Track> change) => CommitEdit(ChangeEdited(change), _ => Selection.Points);

    /// <summary>A scene change that applies <paramref name="change"/> to the edited track, refusing one that swaps the track.</summary>
    private Func<Scene, Scene> ChangeEdited(Func<Track, Track> change) =>
        scene =>
        {
            var before = SceneEditing.Get(scene, EditedTrackId);
            var result = change(before);
            if (ReferenceEquals(result, before))
                return scene;
            if (result.Id != EditedTrackId)
                throw new ArgumentException("A change cannot replace the track.");
            return SceneEditing.Replace(scene, result);
        };

    /// <summary>Applies a change to the scene as one undo step if the edited track can still be played. Returns why it was refused, or null.</summary>
    private string? CommitEdit(Func<Scene, Scene> change, Func<Track, IReadOnlyList<int>> selectAfter)
    {
        Transport.StopPreview();
        if (Mode != CameraMode.Editing)
            return TrackOnlyWhileEditing;
        EndLiveEdit();

        return Refusal(() =>
        {
            var result = change(Scene);
            if (ReferenceEquals(result, Scene))
                return;

            var edited = SceneEditing.Get(result, EditedTrackId);
            _ = new TrackEvaluator(edited);
            history.Record(Current);
            var selected = selectAfter(edited);
            Scene = result;
            Selection.AfterCommit(selected, edited);
        });
    }

    /// <summary>Applies a scene change and the edited track it leaves, as one undo step. Returns why it was refused, or null.</summary>
    private string? CommitScene(Func<Scene, (Scene Scene, Guid Edited)> change)
    {
        Transport.StopPreview();
        if (Mode != CameraMode.Editing)
            return "The scene can only change while editing.";
        EndLiveEdit();

        return Refusal(() =>
        {
            var (result, edited) = change(Scene);
            if (edited == EditedTrackId && (ReferenceEquals(result, Scene) || SameValues(result, Scene)))
                return;

            history.Record(Current);
            if (edited != EditedTrackId)
                ClearForSwitch();
            Scene = result;
            EditedTrackId = edited;
        });
    }

    /// <summary>Runs <paramref name="attempt"/>; returns the refusal an <see cref="ArgumentException"/> carries, or null when it ran.</summary>
    private static string? Refusal(Action attempt)
    {
        try
        {
            attempt();
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private bool Restore(EditSnapshot? snapshot)
    {
        if (snapshot is not { } s)
            return false;
        var pointsBefore = Local.Points;
        if (s.Edited != EditedTrackId)
            ClearForSwitch();
        Scene = s.Scene;
        EditedTrackId = s.Edited;
        Selection.Restore(s.Selection, pointsBefore);
        return true;
    }

    /// <summary>Clears every selection, key and leg and puts the scrub head at 0, as switching tracks does.</summary>
    private void ClearForSwitch()
    {
        Selection.Clear();
        Transport.Park(0.0);
    }

    /// <summary>True when two scenes hold the same anchor, hidden set, playlist, playlist loop and tracks by value.</summary>
    private static bool SameValues(Scene a, Scene b)
    {
        if (
            a.Anchor != b.Anchor
            || a.AnchorPlaced != b.AnchorPlaced
            || a.PlaylistLoops != b.PlaylistLoops
            || a.Tracks.Count != b.Tracks.Count
            || !a.Hidden.SetEquals(b.Hidden)
            || !a.Playlist.SequenceEqual(b.Playlist)
        )
            return false;
        for (var i = 0; i < a.Tracks.Count; i++)
        {
            var x = a.Tracks[i];
            var y = b.Tracks[i];
            if (ReferenceEquals(x, y))
                continue;
            if (
                x with { Points = y.Points, Timing = y.Timing } != y
                || !x.Points.SequenceEqual(y.Points)
                || !x.Timing.SequenceEqual(y.Timing)
            )
                return false;
        }

        return true;
    }
}
