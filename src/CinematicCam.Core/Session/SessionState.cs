using CinematicCam.Core.Camera;
using CinematicCam.Core.Tracks;

namespace CinematicCam.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome { Unchanged, FromOff, FromLive }

/// <summary>What <see cref="SessionState.Play"/> or <see cref="SessionState.Restart"/> did.</summary>
public enum PlayOutcome { Refused, ReHid, Resumed, Started, StartedFromOff }

/// <summary>The mode, the Director and the track, and the rules for moving between modes.</summary>
public sealed class SessionState
{
    private readonly EditHistory history = new();
    private Track? evaluatedTrack;
    private TrackEvaluator? evaluator;
    private double scrubTime;
    private bool resumeAfterScrub;

    public CameraMode Mode { get; private set; }

    public Director Director { get; } = new();

    /// <summary>The track Edit builds and Play plays. Changed only through the edit methods and undo.</summary>
    public Track Track { get; private set; } = TrackEditing.Empty();

    /// <summary>The track's length in seconds: its last timing key, or 0 with none.</summary>
    public double Duration => Track.Timing.Count == 0 ? 0.0 : Track.Timing[^1].Time;

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

        Director.GoLive(new TrackShot(Track));
        var fromOff = Mode == CameraMode.Off;
        Mode = CameraMode.Live;
        return fromOff ? PlayOutcome.StartedFromOff : PlayOutcome.Started;
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

    /// <summary>Selects a point while editing; null or an index out of range clears the selection.</summary>
    public void Select(int? index)
    {
        if (Mode != CameraMode.Editing) return;
        Selected = index is { } i && i >= 0 && i < Track.Points.Count ? i : null;
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
        var s = Selected!.Value;
        return Apply(t => TrackEditing.Delete(t, s), _ => null);
    }

    /// <summary>Moves a point in the order; the selection stays on the same point.</summary>
    public string? MovePoint(int from, int to)
    {
        var selected = Selected;
        return Apply(t => TrackEditing.Move(t, from, to), _ => selected is { } s ? Follow(s, from, to) : null);
    }

    /// <summary>Restores the track and selection before the last change. Returns false if nothing was undone.</summary>
    public bool Undo() => Restore(Mode == CameraMode.Editing ? history.Undo(Current) : null);

    /// <summary>Re-applies the last undone change. Returns false if nothing was redone.</summary>
    public bool Redo() => Restore(Mode == CameraMode.Editing ? history.Redo(Current) : null);

    private EditSnapshot Current => new(Track, Selected);

    private string? Apply(Func<Track, Track> change, Func<Track, int?> selectAfter)
    {
        if (Mode != CameraMode.Editing) return "The track can only change while editing.";

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
        Track = s.Track;
        Selected = s.Selected;
        return true;
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

    /// <summary>The track's frame at <paramref name="time"/> seconds, or null with no points.</summary>
    public CameraState? FrameAt(double time)
    {
        if (Track.Points.Count == 0) return null;
        if (!ReferenceEquals(evaluatedTrack, Track))
        {
            evaluator = new TrackEvaluator(Track);
            evaluatedTrack = Track;
        }

        return evaluator!.Evaluate(time);
    }

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    public bool Scrubbing { get; private set; }

    /// <summary>Seconds under the scrub head: playback time while live, otherwise the last scrubbed or jumped-to time.</summary>
    public double ScrubHead => Mode == CameraMode.Live ? Director.Elapsed : Math.Min(scrubTime, Duration);

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
