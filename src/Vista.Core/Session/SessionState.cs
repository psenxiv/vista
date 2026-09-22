using Vista.Core.Camera;
using Vista.Core.Editing;
using Vista.Core.Tracks;

namespace Vista.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome { Unchanged, FromOff, FromLive }

/// <summary>What <see cref="SessionState.Play"/>, <see cref="SessionState.Restart"/> or <see cref="SessionState.Cue"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff, Cued, CuedFromOff }

/// <summary>The mode, the Director and the track, and the rules for moving between modes.</summary>
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

    public CameraMode Mode { get; private set; }

    public Director Director { get; } = new();

    /// <summary>The track Edit builds and Play plays. Changed only through the edit methods and undo.</summary>
    public Track Track { get; private set; } = TrackEditing.Empty();

    /// <summary>The track's length in seconds: its last compiled key, or 0 with no points.</summary>
    public double Duration => Evaluator.Duration;

    /// <summary>True while the character is locked and flight keys and zoom are blocked.</summary>
    public bool LocksInput => Mode != CameraMode.Off;

    /// <summary>Enters editing; from live, takes the Director offline.</summary>
    public EditOutcome Edit()
    {
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

    /// <summary>Resumes a paused shot, leaves a playing one alone, otherwise restarts.</summary>
    public PlayOutcome Play()
    {
        if (Mode == CameraMode.Live && !Director.IsFinished)
        {
            if (!Director.IsPaused) return PlayOutcome.ReHid;
            Director.Resume();
            return PlayOutcome.Resumed;
        }

        return Restart();
    }

    /// <summary>Goes live with the track from its start. Refused with no points.</summary>
    public PlayOutcome Restart()
    {
        if (Track.Points.Count == 0) return PlayOutcome.Refused;
        Scrubbing = false;
        EndLiveEdit();

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
    }

    /// <summary>Goes live with the track paused at its start. Refused with no points.</summary>
    public PlayOutcome Cue()
    {
        var outcome = Restart();
        if (outcome == PlayOutcome.Refused) return outcome;
        Director.Pause();
        return outcome == PlayOutcome.StartedFromOff ? PlayOutcome.CuedFromOff : PlayOutcome.Cued;
    }

    /// <summary>Holds the current frame and stays live. Returns false unless live.</summary>
    public bool Stop()
    {
        if (Mode != CameraMode.Live) return false;
        Director.Pause();
        return true;
    }

    /// <summary>Turns off and takes the Director offline. Returns false if already off.</summary>
    public bool Release()
    {
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
        Selected = index is { } i && i >= 0 && i < Track.Points.Count ? i : null;
        SyncKeyToPoint();
    }

    /// <summary>Selects a timing key while editing; a point's key also selects its point.</summary>
    public void SelectKey(int? key)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedLeg = null;
        SelectedKey = key is { } k && k >= 0 && k < TrackEditing.KeyCount(Track) ? k : null;
        if (SelectedKey is { } s && TrackEditing.RoleOf(Track, s) == KeyRole.Point) Selected = TrackEditing.PointOf(Track, s);
    }

    /// <summary>Selects a leg while editing, leaving the point selection alone.</summary>
    public void SelectLeg(int? leg)
    {
        if (Mode != CameraMode.Editing) return;
        SelectedKey = null;
        SelectedLeg = leg is { } l && l >= 1 && l < Track.Points.Count ? l : null;
    }

    /// <summary>Applies <paramref name="change"/> if editing and the result can be played. Returns why it was refused, or null once applied.</summary>
    public string? ChangeTrack(Func<Track, Track> change)
        => Apply(change, result => Selected is { } s && s < result.Points.Count ? s : null);

    /// <summary>Appends a point; the selection is unchanged.</summary>
    public string? AddToEnd(ControlPoint point)
        => Apply(t => TrackEditing.Append(t, point), _ => Selected);

    /// <summary>Inserts a point after the selected one and selects it.</summary>
    public string? AddAfterSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        var s = Selected!.Value;
        return Apply(t => TrackEditing.InsertAfter(t, s, point), _ => s + 1);
    }

    /// <summary>Replaces the selected point, keeping its timing and the selection.</summary>
    public string? OverwriteSelected(ControlPoint point)
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        return ReplacePoint(Selected!.Value, point);
    }

    /// <summary>Replaces point <paramref name="index"/>, keeping its timing and the selection.</summary>
    public string? ReplacePoint(int index, ControlPoint point)
        => Apply(t => TrackEditing.Replace(t, index, point), _ => Selected);

    /// <summary>Deletes the selected point and clears the selection.</summary>
    public string? DeleteSelected()
    {
        if (SelectionRefusal() is { } refusal) return refusal;
        return DeletePoint(Selected!.Value);
    }

    /// <summary>Deletes point <paramref name="index"/>; any other selected point stays selected.</summary>
    public string? DeletePoint(int index)
    {
        if (index < 0 || index >= Track.Points.Count) return "There is no such point.";
        var selected = Selected;
        return Apply(t => TrackEditing.Delete(t, index), _ => selected is { } s && s != index ? (s > index ? s - 1 : s) : null);
    }

    /// <summary>Moves a point in the order; the selection stays on the same point.</summary>
    public string? MovePoint(int from, int to)
    {
        var selected = Selected;
        return Apply(t => TrackEditing.Move(t, from, to), _ => selected is { } s ? Follow(s, from, to) : null);
    }

    /// <summary>Restores the track and selection before the last change. Returns false if nothing was undone.</summary>
    public bool Undo()
    {
        EndLiveEdit();
        return Restore(Mode == CameraMode.Editing ? history.Undo(Current) : null);
    }

    /// <summary>Re-applies the last undone change. Returns false if nothing was redone.</summary>
    public bool Redo()
    {
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

    /// <summary>Starts a live edit: previews change the track at once and end as one undo step. Editing only.</summary>
    public void BeginLiveEdit()
    {
        if (Mode == CameraMode.Editing && liveEditStart is null) liveEditStart = Current;
    }

    /// <summary>Replaces point <paramref name="index"/> during a live edit without recording a step. Returns why it was refused, or null.</summary>
    public string? PreviewPoint(int index, ControlPoint point)
    {
        if (liveEditStart is null) return "No live edit is in progress.";
        try
        {
            var result = TrackEditing.Replace(Track, index, point);
            _ = new TrackEvaluator(result);
            Track = result;
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

    /// <summary>Ends a live edit, recording it as one undo step if the track changed.</summary>
    public void EndLiveEdit()
    {
        if (liveEditStart is not { } start) return;
        liveEditStart = null;
        if (ReferenceEquals(start.Track, Track)) return;

        // Previews rebuild the lists, so compare values: a drag back to the start is no step.
        if (start.Track.Points.SequenceEqual(Track.Points) && start.Track.Timing.SequenceEqual(Track.Timing) && start.Track.Speed == Track.Speed) Track = start.Track;
        else history.Record(start);
    }

    private EditSnapshot Current => new(Track, Selected);

    private string? Apply(Func<Track, Track> change, Func<Track, int?> selectAfter)
    {
        var pointsBefore = Track.Points;
        var refusal = Commit(change, selectAfter);
        if (refusal is null) RefreshTimingSelection(pointsBefore);
        return refusal;
    }

    /// <summary>Applies a timing change, keeping the point selection and any timing selection still in range.</summary>
    private string? ApplyTiming(Func<Track, Track> change)
    {
        var refusal = Commit(change, _ => Selected);
        if (refusal is not null) return refusal;
        if (SelectedKey is { } key && key >= TrackEditing.KeyCount(Track)) SelectedKey = null;
        if (SelectedLeg is { } leg && leg >= Track.Points.Count) SelectedLeg = null;
        return null;
    }

    private string? Commit(Func<Track, Track> change, Func<Track, int?> selectAfter)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";
        EndLiveEdit();

        try
        {
            var result = change(Track);
            if (ReferenceEquals(result, Track)) return null;

            _ = new TrackEvaluator(result);
            history.Record(Current);
            var selected = selectAfter(result);
            Track = result;
            Selected = selected;
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private bool Restore(EditSnapshot? snapshot)
    {
        if (snapshot is not { } s) return false;
        var pointsBefore = Track.Points;
        Track = s.Track;
        Selected = s.Selected;
        RefreshTimingSelection(pointsBefore);
        return true;
    }

    /// <summary>Points the timing selection at the selected point's key, or clears a point key's selection when no point is selected.</summary>
    private void SyncKeyToPoint()
    {
        if (Selected is { } point)
        {
            SelectedKey = TrackEditing.PointKey(Track, point);
            SelectedLeg = null;
        }
        else if (SelectedKey is { } key && (key >= TrackEditing.KeyCount(Track) || TrackEditing.RoleOf(Track, key) == KeyRole.Point))
        {
            SelectedKey = null;
        }
    }

    /// <summary>After a point edit: a point key follows its point, and any other timing selection clears unless it's a leg and the points are unchanged.</summary>
    private void RefreshTimingSelection(IReadOnlyList<ControlPoint> pointsBefore)
    {
        if (!ReferenceEquals(pointsBefore, Track.Points) && !pointsBefore.SequenceEqual(Track.Points)) SelectedLeg = null;
        if (SelectedKey is { } key && (Selected is null || key >= TrackEditing.KeyCount(Track) || TrackEditing.RoleOf(Track, key) != KeyRole.Point)) SelectedKey = null;
        if (Selected is not null && SelectedLeg is null) SelectedKey = TrackEditing.PointKey(Track, Selected.Value);
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
        if (!ReferenceEquals(evaluatedStart, start.Track))
        {
            liveStartEvaluator = new TrackEvaluator(start.Track);
            evaluatedStart = start.Track;
        }

        try
        {
            var result = change(start.Track, liveStartEvaluator!);
            _ = new TrackEvaluator(result);
            Track = result;
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
    public CameraState? FrameAt(double time) => Track.Points.Count == 0 ? null : Evaluator.Evaluate(time);

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Seconds under the scrub head: playback time while live, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => Mode == CameraMode.Live ? Director.ShotTime : Math.Min(scrubTime, Duration);

    /// <summary>Starts dragging the scrub head; live, playback holds until <see cref="EndScrub"/>. No effect when off.</summary>
    public void BeginScrub()
    {
        if (Mode == CameraMode.Off || Scrubbing) return;
        Scrubbing = true;
        resumeAfterScrub = Mode == CameraMode.Live && !Director.IsPaused;
        if (Mode == CameraMode.Live) Director.Pause();
    }

    /// <summary>Moves the scrub head to <paramref name="time"/> within the track; live, playback seeks there. No effect when off.</summary>
    public void ScrubTo(double time)
    {
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
