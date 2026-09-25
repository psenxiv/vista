using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionEditingTests
{
    [Fact]
    public void AddToEndKeepsTheSelection()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        Assert.Null(state.AddToEnd(Point(30f)));
        Assert.Equal(1, state.Selection.Point);

        state.Selection.Select(null);
        state.AddToEnd(Point(40f));
        Assert.Null(state.Selection.Point);
    }

    [Fact]
    public void AddAfterSelectedSelectsTheNewPoint()
    {
        var state = EditingThreePoints();
        state.Selection.Select(0);
        var point = Point(5f);

        Assert.Null(state.AddAfterSelected(point));
        Assert.Equal(1, state.Selection.Point);
        Assert.Equal(point, state.Track.Points[1]);

        state.AddAfterSelected(Point(7f));
        Assert.Equal(2, state.Selection.Point);
        Assert.Equal(7f, state.Track.Points[2].Position.X);
    }

    [Fact]
    public void SelectedOnlyEditsNeedASelection()
    {
        var state = EditingThreePoints();
        const string refused = "Select a point first.";
        Assert.Equal(refused, state.AddAfterSelected(Point(5f)));
        Assert.Equal(refused, state.OverwriteSelected(Point(5f)));
        Assert.Equal(refused, state.DeleteSelected());
    }

    [Fact]
    public void OverwriteSelectedKeepsSelectionAndTiming()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        var timing = state.Track.Timing;

        Assert.Null(state.OverwriteSelected(Point(12f)));
        Assert.Equal(1, state.Selection.Point);
        Assert.Equal(12f, state.Track.Points[1].Position.X);
        Assert.Same(timing, state.Track.Timing);
    }

    [Fact]
    public void OverwritingWithAnEqualPointRecordsNoUndoStep()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.OverwriteSelected(state.Track.Points[1] with { });

        var steps = 0;
        while (state.Undo())
            steps++;
        Assert.Equal(4, steps); // the speed and three points
    }

    [Fact]
    public void ReplacePointKeepsTheSelection()
    {
        var state = EditingThreePoints();
        state.Selection.Select(2);
        Assert.Null(state.ReplacePoint(0, Point(-1f)));
        Assert.Equal(2, state.Selection.Point);
        Assert.Equal(-1f, state.Track.Points[0].Position.X);
    }

    [Fact]
    public void DeleteSelectedClearsTheSelection()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        Assert.Null(state.DeleteSelected());
        Assert.Null(state.Selection.Point);
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Theory]
    [InlineData(1, 0, 0)] // a point before it
    [InlineData(1, 2, 1)] // a point after it
    [InlineData(1, 1, null)] // the selected point itself
    [InlineData(null, 1, null)] // nothing selected
    public void DeletePointKeepsTheSelectionOnTheSamePoint(int? selected, int index, int? expected)
    {
        var state = EditingThreePoints();
        state.Selection.Select(selected);
        Assert.Null(state.DeletePoints([index]));
        Assert.Equal(2, state.Track.Points.Count);
        Assert.Equal(expected, state.Selection.Point);
    }

    [Fact]
    public void UndoingADeleteRestoresTheSelection()
    {
        var state = EditingThreePoints();
        state.Selection.Select(2);
        state.DeletePoints([0]);
        Assert.True(state.Undo());
        Assert.Equal(3, state.Track.Points.Count);
        Assert.Equal(2, state.Selection.Point);
    }

    [Fact]
    public void DeletePointOutOfRangeIsRefused()
    {
        var state = EditingThreePoints();
        Assert.NotNull(state.DeletePoints([3]));
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void EditFromLiveMovesTheScrubHeadToThePlaybackTime()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        state.Director.Tick(2f);
        state.Edit();
        Assert.Equal(2.0, state.Transport.ScrubHead, 5);
    }

    [Fact]
    public void CueingAReverseShotPutsTheScrubHeadAtTheEnd()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        state.Cue();
        Assert.Equal(10.0, state.Transport.ScrubHead, 5);
    }

    [Fact]
    public void EditFromALiveReverseShotTakesItsShotTime()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.Reverse));
        GoLive(state);
        state.Director.Tick(2f);
        state.Edit();
        Assert.Equal(8.0, state.Transport.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingALivePingPongShotOnItsWayBackKeepsItGoingBack()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.ChangeTrack(t => TrackEditing.SetDirection(t, PlaybackDirection.PingPong));
        GoLive(state);
        state.Director.Tick(13f);
        Assert.Equal(7.0, state.Transport.ScrubHead, 3);

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(4.0);
        state.Transport.EndScrub();
        state.Director.Tick(1f);
        Assert.Equal(3.0, state.Transport.ScrubHead, 3);
    }

    [Theory]
    [InlineData(1, 1, 3, 3)] // the selected point itself moves
    [InlineData(1, 0, 2, 0)] // a point before it moves past it
    [InlineData(1, 3, 0, 2)] // a point after it moves before it
    [InlineData(1, 2, 3, 1)] // a move entirely after it
    public void MoveKeepsTheSelectionOnTheSamePoint(int selected, int from, int to, int expected)
    {
        var state = EditingThreePoints();
        state.AddToEnd(Point(30f));
        state.Selection.Select(selected);
        var point = state.Track.Points[selected];

        Assert.Null(state.MovePoints([from], from, to));
        Assert.Equal(expected, state.Selection.Point);
        Assert.Same(point, state.Track.Points[expected]);
    }

    [Fact]
    public void ChangeTrackClearsASelectionThatNoLongerExists()
    {
        var state = EditingThreePoints();
        state.Selection.Select(2);
        state.ChangeTrack(TrackEditing.Clear);
        Assert.Null(state.Selection.Point);
    }

    [Theory]
    [InlineData(3)] // one past the last of three points
    [InlineData(5)]
    public void SelectOutOfRangeClears(int index)
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.Selection.Select(index);
        Assert.Null(state.Selection.Point);
    }

    [Fact]
    public void UndoAndRedoRestoreTrackAndSelection()
    {
        var state = EditingThreePoints();
        state.Selection.Select(0);
        var before = state.Track;
        state.AddAfterSelected(Point(5f));
        var after = state.Track;

        Assert.True(state.Undo());
        Assert.Same(before, state.Track);
        Assert.Equal(0, state.Selection.Point);

        Assert.True(state.Redo());
        Assert.Same(after, state.Track);
        Assert.Equal(1, state.Selection.Point);
    }

    [Fact]
    public void UndoRestoresTheSelectionAfterAnOverwrite()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.OverwriteSelected(Point(12f));

        state.Undo();
        Assert.Equal(1, state.Selection.Point);
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ANewChangeClearsRedo()
    {
        var state = EditingThreePoints();
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
        var state = EditingThreePoints();
        state.MovePoints([1], 1, 1);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void UndoRedoAndSelectWorkOnlyWhileEditing()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Selection.Select(1);
        GoLive(state);

        Assert.False(state.CanUndo);
        Assert.False(state.Undo());
        state.Selection.Select(0);
        Assert.Equal(1, state.Selection.Point);
        Assert.Equal("The track can only change while editing.", state.AddToEnd(Point(30f)));

        state.Edit();
        Assert.True(state.Undo());
    }

    [Fact]
    public void FrameAtIsTheCameraOnThePathAndNullForAnEmptyTrack()
    {
        Assert.Null(new SessionState().World.FrameAt(1.0));

        // 2.5 s at 2 yalms per second is 5 yalms along, to within the arc-length table's resolution.
        var frame = EditingThreePoints().World.FrameAt(2.5)!.Value;

        Assert.Equal(5f, frame.Position.X, 0.001f);
        Assert.Equal(0f, frame.Position.Y, 1e-4f);
        Assert.Equal(0f, frame.Position.Z, 1e-4f);
        Assert.Equal(5f, frame.LookAt.X, 0.001f);
        Assert.Equal(0f, frame.LookAt.Y, 1e-4f);
        Assert.Equal(-10f, frame.LookAt.Z, 1e-4f);
        Assert.Equal(1f, frame.Fov, 1e-4f);
        Assert.Equal(0f, frame.Roll, 1e-4f);
    }

    [Fact]
    public void DurationIsTheLastKeysTimeAndZeroWhenEmpty()
    {
        var state = new SessionState();
        Assert.Equal(0.0, state.Duration);

        state = EditingThreePoints();
        Assert.Equal(10.0, state.Duration, 5);

        state.ChangeTrack(t => TrackEditing.SetHold(t, 2, 2f));
        Assert.Equal(12.0, state.Duration, 5);

        state.ChangeTrack(t => TrackEditing.SetSpeed(t, 4f));
        Assert.Equal(7.0, state.Duration, 3);
    }

    [Fact]
    public void TheScrubHeadWhileEditingIsTheLastScrubbedTimeWithinTheTrack()
    {
        var state = EditingThreePoints();
        Assert.Equal(0.0, state.Transport.ScrubHead);
        state.Transport.ScrubTo(4.0);
        Assert.Equal(4.0, state.Transport.ScrubHead);
        state.Transport.ScrubTo(99.0);
        Assert.Equal(10.0, state.Transport.ScrubHead);
        state.Transport.ScrubTo(-1.0);
        Assert.Equal(0.0, state.Transport.ScrubHead);
    }

    [Fact]
    public void ScrubbingWhileEditingSetsScrubbingUntilItEnds()
    {
        var state = EditingThreePoints();
        state.Transport.BeginScrub();
        Assert.True(state.Transport.Scrubbing);
        state.Transport.EndScrub();
        Assert.False(state.Transport.Scrubbing);
    }

    [Fact]
    public void ScrubbingLiveHoldsPlaybackThenResumesIt()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        state.Transport.BeginScrub();
        Assert.True(state.Director.IsPaused);
        state.Transport.ScrubTo(6.0);
        Assert.Equal(6.0, state.Transport.ScrubHead, 5);
        state.Transport.EndScrub();
        Assert.False(state.Director.IsPaused);
        Assert.False(state.Transport.Scrubbing);
    }

    [Fact]
    public void ScrubbingAPausedShotLeavesItPaused()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        state.Stop();
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(2.0);
        state.Transport.EndScrub();
        Assert.True(state.Director.IsPaused);
        Assert.Equal(2.0, state.Transport.ScrubHead, 5);
    }

    [Fact]
    public void ScrubbingAFinishedForwardShotBackUnfinishesIt()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        state.Director.Tick(20f);
        Assert.True(state.Director.IsFinished);

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(3.0);
        state.Transport.EndScrub();
        Assert.False(state.Director.IsFinished);
        Assert.False(state.Director.IsPaused);
    }

    [Fact]
    public void ModeChangesEndAScrub()
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Transport.BeginScrub();
        GoLive(state);
        Assert.False(state.Transport.Scrubbing);

        state.Transport.BeginScrub();
        state.Edit();
        Assert.False(state.Transport.Scrubbing);

        state.Transport.BeginScrub();
        state.Release();
        Assert.False(state.Transport.Scrubbing);
    }

    [Fact]
    public void ScrubbingDoesNothingInView()
    {
        var state = new SessionState();
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(3.0);
        Assert.False(state.Transport.Scrubbing);
        Assert.Equal(0.0, state.Transport.ScrubHead);
    }

    [Fact]
    public void TheScrubHeadStaysWithinATrackThatGotShorter()
    {
        var state = EditingThreePoints();
        state.Transport.ScrubTo(10.0);
        state.ChangeTrack(TrackEditing.Clear);
        Assert.Equal(0.0, state.Transport.ScrubHead);
    }

    [Fact]
    public void ALivePointEditIsOneUndoStep()
    {
        var state = EditingThreePoints();
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
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        state.EndLiveEdit();
        state.Undo();
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void PreviewingWithoutALiveEditIsRefused()
    {
        var state = EditingThreePoints();
        Assert.NotNull(state.PreviewPoint(1, Point(99f)));
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void UndoInTheMiddleOfALiveEditRevertsIt()
    {
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        Assert.True(state.Undo());
        Assert.Equal(10f, state.Track.Points[1].Position.X);
        Assert.NotNull(state.PreviewPoint(1, Point(12f)));
    }

    [Fact]
    public void AModeChangeEndsALiveEditAsAStep()
    {
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(11f));
        GoLive(state);
        state.Edit();
        Assert.True(state.Undo());
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ALiveEditThatEndsWhereItStartedRecordsNoStepAndKeepsRedo()
    {
        var state = EditingThreePoints();
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
        var state = EditingThreePoints();
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
