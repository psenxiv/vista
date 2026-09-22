using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionEditingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing at 2 yalms per second, three points at x = 0, 10, 20.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        return state;
    }

    [Fact]
    public void AddToEndKeepsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.AddToEnd(Point(30f)));
        Assert.Equal(1, state.Selected);

        state.Select(null);
        state.AddToEnd(Point(40f));
        Assert.Null(state.Selected);
    }

    [Fact]
    public void AddAfterSelectedSelectsTheNewPoint()
    {
        var state = Editing();
        state.Select(0);
        var point = Point(5f);

        Assert.Null(state.AddAfterSelected(point));
        Assert.Equal(1, state.Selected);
        Assert.Equal(point, state.Track.Points[1]);

        state.AddAfterSelected(Point(7f));
        Assert.Equal(2, state.Selected);
        Assert.Equal(7f, state.Track.Points[2].Position.X);
    }

    [Fact]
    public void SelectedOnlyEditsNeedASelection()
    {
        var state = Editing();
        const string refused = "Select a point first.";
        Assert.Equal(refused, state.AddAfterSelected(Point(5f)));
        Assert.Equal(refused, state.OverwriteSelected(Point(5f)));
        Assert.Equal(refused, state.DeleteSelected());
    }

    [Fact]
    public void OverwriteSelectedKeepsSelectionAndTiming()
    {
        var state = Editing();
        state.Select(1);
        var timing = state.Track.Timing;

        Assert.Null(state.OverwriteSelected(Point(12f)));
        Assert.Equal(1, state.Selected);
        Assert.Equal(12f, state.Track.Points[1].Position.X);
        Assert.Same(timing, state.Track.Timing);
    }

    [Fact]
    public void OverwritingWithAnEqualPointRecordsNoUndoStep()
    {
        var state = Editing();
        state.Select(1);
        state.OverwriteSelected(state.Track.Points[1] with { });

        var steps = 0;
        while (state.Undo()) steps++;
        Assert.Equal(4, steps); // the speed and three points
    }

    [Fact]
    public void ReplacePointKeepsTheSelection()
    {
        var state = Editing();
        state.Select(2);
        Assert.Null(state.ReplacePoint(0, Point(-1f)));
        Assert.Equal(2, state.Selected);
        Assert.Equal(-1f, state.Track.Points[0].Position.X);
    }

    [Fact]
    public void DeleteSelectedClearsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.DeleteSelected());
        Assert.Null(state.Selected);
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Theory]
    [InlineData(1, 0, 0)] // a point before it
    [InlineData(1, 2, 1)] // a point after it
    [InlineData(1, 1, null)] // the selected point itself
    [InlineData(null, 1, null)] // nothing selected
    public void DeletePointKeepsTheSelectionOnTheSamePoint(int? selected, int index, int? expected)
    {
        var state = Editing();
        state.Select(selected);
        Assert.Null(state.DeletePoint(index));
        Assert.Equal(2, state.Track.Points.Count);
        Assert.Equal(expected, state.Selected);
    }

    [Fact]
    public void UndoingADeleteRestoresTheSelection()
    {
        var state = Editing();
        state.Select(2);
        state.DeletePoint(0);
        Assert.True(state.Undo());
        Assert.Equal(3, state.Track.Points.Count);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void DeletePointOutOfRangeIsRefused()
    {
        var state = Editing();
        Assert.NotNull(state.DeletePoint(3));
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void EditFromLiveMovesTheScrubHeadToThePlaybackTime()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        state.Director.Tick(2f);
        state.Edit();
        Assert.Equal(2.0, state.ScrubHead, 5);
    }

    [Fact]
    public void CueingAReverseShotPutsTheScrubHeadAtTheEnd()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        state.Cue();
        Assert.Equal(10.0, state.ScrubHead, 5);
    }

    [Fact]
    public void EditFromALiveReverseShotTakesItsShotTime()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        state.Cue();
        state.Play();
        state.Director.Tick(2f);
        state.Edit();
        Assert.Equal(8.0, state.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingALivePingPongShotOnItsWayBackKeepsItGoingBack()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.PingPong));
        state.Cue();
        state.Play();
        state.Director.Tick(13f);
        Assert.Equal(7.0, state.ScrubHead, 3);

        state.BeginScrub();
        state.ScrubTo(4.0);
        state.EndScrub();
        state.Director.Tick(1f);
        Assert.Equal(3.0, state.ScrubHead, 3);
    }

    [Theory]
    [InlineData(1, 1, 3, 3)] // the selected point itself moves
    [InlineData(1, 0, 2, 0)] // a point before it moves past it
    [InlineData(1, 3, 0, 2)] // a point after it moves before it
    [InlineData(1, 2, 3, 1)] // a move entirely after it
    public void MoveKeepsTheSelectionOnTheSamePoint(int selected, int from, int to, int expected)
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Select(selected);
        var point = state.Track.Points[selected];

        Assert.Null(state.MovePoint(from, to));
        Assert.Equal(expected, state.Selected);
        Assert.Same(point, state.Track.Points[expected]);
    }

    [Fact]
    public void ChangeTrackClearsASelectionThatNoLongerExists()
    {
        var state = Editing();
        state.Select(2);
        state.ChangeTrack(TrackEditing.Clear);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void SelectOutOfRangeClears()
    {
        var state = Editing();
        state.Select(1);
        state.Select(5);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void UndoAndRedoRestoreTrackAndSelection()
    {
        var state = Editing();
        state.Select(0);
        var before = state.Track;
        state.AddAfterSelected(Point(5f));
        var after = state.Track;

        Assert.True(state.Undo());
        Assert.Same(before, state.Track);
        Assert.Equal(0, state.Selected);

        Assert.True(state.Redo());
        Assert.Same(after, state.Track);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void UndoRestoresTheSelectionAfterAnOverwrite()
    {
        var state = Editing();
        state.Select(1);
        state.OverwriteSelected(Point(12f));

        state.Undo();
        Assert.Equal(1, state.Selected);
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ANewChangeClearsRedo()
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Undo();
        state.AddToEnd(Point(40f));

        Assert.False(state.CanRedo);
        Assert.False(state.Redo());
    }

    [Fact]
    public void RefusedChangesRecordNothing()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.DeleteSelected());
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void AChangeThatChangesNothingRecordsNothing()
    {
        var state = Editing();
        state.MovePoint(1, 1);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void UndoRedoAndSelectWorkOnlyWhileEditing()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.Select(1);
        state.Cue();
        state.Play();

        Assert.False(state.CanUndo);
        Assert.False(state.Undo());
        state.Select(0);
        Assert.Equal(1, state.Selected);
        Assert.Equal("The track can only change while editing.", state.AddToEnd(Point(30f)));

        state.Edit();
        Assert.True(state.Undo());
    }

    [Fact]
    public void FrameAtMatchesTheEvaluatorAndIsNullForAnEmptyTrack()
    {
        Assert.Null(new SessionState().FrameAt(1.0));

        var state = Editing();
        var expected = new TrackEvaluator(state.Track).Evaluate(2.5);
        Assert.Equal(expected, state.FrameAt(2.5));
    }

    [Fact]
    public void DurationIsTheLastKeysTimeAndZeroWhenEmpty()
    {
        var state = new SessionState();
        Assert.Equal(0.0, state.Duration);

        state = Editing();
        Assert.Equal(10.0, state.Duration, 5);

        state.ChangeTrack(t => TrackEditing.SetHold(t, 2, 2f));
        Assert.Equal(12.0, state.Duration, 5);

        state.ChangeTrack(t => TrackEditing.SetSpeed(t, 4f));
        Assert.Equal(7.0, state.Duration, 3);
    }

    [Fact]
    public void TheScrubHeadWhileEditingIsTheLastScrubbedTimeWithinTheTrack()
    {
        var state = Editing();
        Assert.Equal(0.0, state.ScrubHead);
        state.ScrubTo(4.0);
        Assert.Equal(4.0, state.ScrubHead);
        state.ScrubTo(99.0);
        Assert.Equal(10.0, state.ScrubHead);
        state.ScrubTo(-1.0);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void ScrubbingWhileEditingSetsScrubbingUntilItEnds()
    {
        var state = Editing();
        state.BeginScrub();
        Assert.True(state.Scrubbing);
        state.EndScrub();
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingLiveHoldsPlaybackThenResumesIt()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        state.BeginScrub();
        Assert.True(state.Director.IsPaused);
        state.ScrubTo(6.0);
        Assert.Equal(6.0, state.ScrubHead, 5);
        state.EndScrub();
        Assert.False(state.Director.IsPaused);
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingAPausedShotLeavesItPaused()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        state.Stop();
        state.BeginScrub();
        state.ScrubTo(2.0);
        state.EndScrub();
        Assert.True(state.Director.IsPaused);
        Assert.Equal(2.0, state.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingAFinishedForwardShotBackUnfinishesIt()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.Cue();
        state.Play();
        state.Director.Tick(20f);
        Assert.True(state.Director.IsFinished);

        state.BeginScrub();
        state.ScrubTo(3.0);
        state.EndScrub();
        Assert.False(state.Director.IsFinished);
        Assert.False(state.Director.IsPaused);
    }

    [Fact]
    public void ModeChangesEndAScrub()
    {
        var state = Editing();
        state.AddToPlaylist(state.EditedTrackId);
        state.BeginScrub();
        state.Cue();
        state.Play();
        Assert.False(state.Scrubbing);

        state.BeginScrub();
        state.Edit();
        Assert.False(state.Scrubbing);

        state.BeginScrub();
        state.Release();
        Assert.False(state.Scrubbing);
    }

    [Fact]
    public void ScrubbingDoesNothingInView()
    {
        var state = new SessionState();
        state.BeginScrub();
        state.ScrubTo(3.0);
        Assert.False(state.Scrubbing);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void TheScrubHeadStaysWithinATrackThatGotShorter()
    {
        var state = Editing();
        state.ScrubTo(10.0);
        state.ChangeTrack(TrackEditing.Clear);
        Assert.Equal(0.0, state.ScrubHead);
    }

    [Fact]
    public void ALivePointEditIsOneUndoStep()
    {
        var state = Editing();
        var original = state.Track.Points[1];
        state.BeginLiveEdit();
        Assert.Null(state.PreviewPoint(1, Point(11f)));
        Assert.Null(state.PreviewPoint(1, Point(12f)));
        Assert.Equal(12f, state.Track.Points[1].Position.X);
        state.EndLiveEdit();

        Assert.True(state.Undo());
        Assert.Equal(original, state.Track.Points[1]);
        Assert.Equal(3, state.Track.Points.Count);
        Assert.True(state.CanRedo);
    }

    [Fact]
    public void AnUnchangedLivePointEditRecordsNoStep()
    {
        var state = Editing();
        state.BeginLiveEdit();
        state.EndLiveEdit();
        state.Undo();
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void PreviewingWithoutALiveEditIsRefused()
    {
        var state = Editing();
        Assert.NotNull(state.PreviewPoint(1, Point(99f)));
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void UndoInTheMiddleOfALiveEditRevertsIt()
    {
        var state = Editing();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        Assert.True(state.Undo());
        Assert.Equal(10f, state.Track.Points[1].Position.X);
        Assert.NotNull(state.PreviewPoint(1, Point(12f)));
    }

    [Fact]
    public void AModeChangeEndsALiveEditAsAStep()
    {
        var state = Editing();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        state.Cue();
        state.Play();
        state.Edit();
        Assert.True(state.Undo());
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ALiveEditThatEndsWhereItStartedRecordsNoStepAndKeepsRedo()
    {
        var state = Editing();
        state.Undo();
        Assert.True(state.CanRedo);

        var original = state.Track.Points[1];
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        state.PreviewPoint(1, original);
        state.EndLiveEdit();

        Assert.True(state.CanRedo);
        Assert.True(state.Redo());
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void AnEditInTheMiddleOfALiveEditMakesTwoSteps()
    {
        var state = Editing();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        state.AddToEnd(Point(30f));

        state.Undo();
        Assert.Equal(3, state.Track.Points.Count);
        Assert.Equal(11f, state.Track.Points[1].Position.X);
        state.Undo();
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }
}
