using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionSelectionTests
{
    // Editing Track 1, with points at x = 5, 10, 20, 30, then Tracks 2 and 3, empty, and a playlist entry for each track.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        foreach (var x in new[] { 5f, 10f, 20f, 30f }) state.AddToEnd(Point(x));
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddTrack();
        state.SwitchTrack(first);
        state.AddToPlaylist(state.Scene.Tracks.Select(t => t.Id).ToArray());
        return state;
    }

    private static Guid Track(SessionState state, int index) => state.Scene.Tracks[index].Id;

    private static Guid Entry(SessionState state, int index) => state.Scene.Playlist[index].Id;

    [Fact]
    public void CtrlClickingPointsSelectsSeveralAndClearsTheTimingSelection()
    {
        var state = Editing();
        state.Select(1);
        state.SelectLeg(2);

        state.ClickPoint(3, RowClick.Toggle);

        Assert.Equal([1, 3], state.SelectedPoints);
        Assert.Null(state.Selected);
        Assert.Null(state.SelectedKey);
        Assert.Null(state.SelectedLeg);
    }

    [Fact]
    public void ShiftClickingPointsRangesFromTheLastClicked()
    {
        var state = Editing();
        state.Select(0);

        state.ClickPoint(2, RowClick.Range);

        Assert.Equal([0, 1, 2], state.SelectedPoints);
    }

    [Fact]
    public void TwoPointsBlockCtrlClickingATrackOrAnEntry()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        Assert.Null(state.ClickTrack(Track(state, 1), RowClick.Toggle));
        Assert.Null(state.ClickEntry(Entry(state, 0), RowClick.Range));

        Assert.Equal([0, 1], state.SelectedPoints);
        Assert.Equal([Track(state, 0)], state.SelectedTracks);
        Assert.Empty(state.SelectedEntries);
    }

    [Fact]
    public void OnePointGivesWayToACtrlClickedTrack()
    {
        var state = Editing();
        state.Select(2);

        Assert.Null(state.ClickTrack(Track(state, 2), RowClick.Toggle));

        Assert.Empty(state.SelectedPoints);
        Assert.Equal([Track(state, 0), Track(state, 2)], state.SelectedTracks);
        Assert.Equal(Track(state, 0), state.EditedTrackId);
    }

    [Fact]
    public void TwoTracksBlockCtrlClickingAPointOrAnEntry()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.ClickPoint(0, RowClick.Toggle);
        state.ClickEntry(Entry(state, 0), RowClick.Toggle);

        Assert.Empty(state.SelectedPoints);
        Assert.Empty(state.SelectedEntries);
        Assert.Equal([Track(state, 0), Track(state, 1)], state.SelectedTracks);
    }

    [Fact]
    public void TwoEntriesBlockCtrlClickingAPointOrATrack()
    {
        var state = Editing();
        state.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.ClickEntry(Entry(state, 2), RowClick.Toggle);

        state.ClickPoint(0, RowClick.Toggle);
        state.ClickTrack(Track(state, 1), RowClick.Toggle);

        Assert.Equal([Entry(state, 0), Entry(state, 2)], state.SelectedEntries);
        Assert.Empty(state.SelectedPoints);
        Assert.Equal([Track(state, 0)], state.SelectedTracks);
    }

    [Fact]
    public void CtrlClickingTheEditedTrackLeavesItSelected()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.ClickTrack(Track(state, 0), RowClick.Toggle);

        Assert.Equal([Track(state, 0), Track(state, 1)], state.SelectedTracks);
    }

    [Fact]
    public void CtrlClickingTheEditedTrackKeepsASelectedPoint()
    {
        var state = Editing();
        state.Select(1);

        state.ClickTrack(Track(state, 0), RowClick.Toggle);

        Assert.Equal([1], state.SelectedPoints);
    }

    [Fact]
    public void ShiftClickingAPointAfterCtrlClickingTheLastOneOffStartsAfresh()
    {
        var state = Editing();
        state.Select(1);
        state.ClickPoint(1, RowClick.Toggle);

        state.ClickPoint(3, RowClick.Range);

        Assert.Equal([3], state.SelectedPoints);
    }

    [Fact]
    public void ShiftClickingAnEntryAfterCtrlClickingTheLastOneOffStartsAfresh()
    {
        var state = Editing();
        state.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.ClickEntry(Entry(state, 0), RowClick.Toggle);

        state.ClickEntry(Entry(state, 2), RowClick.Range);

        Assert.Equal([Entry(state, 2)], state.SelectedEntries);
    }

    [Fact]
    public void ShiftClickingATrackRangesFromTheEditedOneOnceTheTracksWereCleared()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.Select(0);

        state.ClickTrack(Track(state, 2), RowClick.Range);

        Assert.Equal([Track(state, 0), Track(state, 1), Track(state, 2)], state.SelectedTracks);
    }

    [Fact]
    public void APlainClickOnATrackEditsItAndClearsTheRest()
    {
        var state = Editing();
        state.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.ClickEntry(Entry(state, 1), RowClick.Toggle);

        Assert.Null(state.ClickTrack(Track(state, 1), RowClick.Plain));

        Assert.Equal(Track(state, 1), state.EditedTrackId);
        Assert.Equal([Track(state, 1)], state.SelectedTracks);
        Assert.Empty(state.SelectedEntries);
    }

    [Fact]
    public void APlainClickOnAnEntryClearsThePoints()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        Assert.Null(state.ClickEntry(Entry(state, 1), RowClick.Plain));

        Assert.Empty(state.SelectedPoints);
        Assert.Equal([Entry(state, 1)], state.SelectedEntries);
    }

    [Fact]
    public void SelectingNothingClearsEveryKind()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 2), RowClick.Toggle);

        state.Select(null);

        Assert.Equal([Track(state, 0)], state.SelectedTracks);
    }

    [Fact]
    public void ARowClickClearsASelectedAnchor()
    {
        var state = Editing();
        Assert.Null(state.SelectSceneAnchor());

        state.ClickEntry(Entry(state, 0), RowClick.Toggle);

        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void ClicksOutsideEditChangeNothing()
    {
        var state = Editing();
        state.Select(0);
        state.Release();

        state.ClickPoint(1, RowClick.Toggle);

        Assert.Equal("Tracks can only be selected while editing.", state.ClickTrack(Track(state, 1), RowClick.Toggle));
        Assert.Equal("Playlist entries can only be selected while editing.", state.ClickEntry(Entry(state, 0), RowClick.Toggle));
        Assert.Equal([0], state.SelectedPoints);
    }

    [Fact]
    public void DeletingTheSelectedPointsIsOneUndoStepThatBringsThemBack()
    {
        var state = Editing();
        state.Select(1);
        state.ClickPoint(3, RowClick.Toggle);

        Assert.Null(state.DeleteSelected());
        Assert.Equal(2, state.Track.Points.Count);
        Assert.Equal(5f, state.Track.Points[0].Position.X, 0.001f);
        Assert.Equal(20f, state.Track.Points[1].Position.X, 0.001f);
        Assert.Empty(state.SelectedPoints);

        Assert.True(state.Undo());
        Assert.Equal(4, state.Track.Points.Count);
        Assert.Equal([1, 3], state.SelectedPoints);
    }

    [Fact]
    public void DeletingSomePointsShiftsTheOthersStillSelected()
    {
        var state = Editing();
        state.Select(1);
        state.ClickPoint(3, RowClick.Toggle);

        // Point 0 goes: 1 and 3 become 0 and 2.
        Assert.Null(state.DeletePoints([0]));

        Assert.Equal([0, 2], state.SelectedPoints);
    }

    [Fact]
    public void AddingAfterOrOverwritingNeedsOnePoint()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        Assert.Equal("Select one point first.", state.AddAfterSelected(Point(0f)));
        Assert.Equal("Select one point first.", state.OverwriteSelected(Point(0f)));
    }

    [Fact]
    public void MovingPointsAsABlockKeepsThemSelected()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        // 0 and 1 grabbed by 1 onto 3, below: after it, so 2 3 0 1.
        Assert.Null(state.MovePoints(state.SelectedPoints, 1, 3));

        Assert.Equal(4, state.Track.Points.Count);
        Assert.Equal(20f, state.Track.Points[0].Position.X, 0.001f);
        Assert.Equal(30f, state.Track.Points[1].Position.X, 0.001f);
        Assert.Equal(5f, state.Track.Points[2].Position.X, 0.001f);
        Assert.Equal(10f, state.Track.Points[3].Position.X, 0.001f);
        Assert.Equal([2, 3], state.SelectedPoints);
    }

    [Fact]
    public void MovingPointsToANewTrackEditsItWithThemSelectedWhereTheyWere()
    {
        var state = Editing();
        var source = state.EditedTrackId;

        Assert.Null(state.MovePointsTo([1, 2], null));

        // The new Track 4, at the end, holds the points at x = 10 and 20 in the world; the source's anchor sits at x = 5.
        Assert.Equal(state.Scene.Tracks[3].Id, state.EditedTrackId);
        Assert.Equal("Track 4", state.Scene.Tracks[3].Name);
        Assert.Equal([0, 1], state.SelectedPoints);
        Assert.Equal(10f, state.Track.Points[0].Position.X, 0.001f);
        Assert.Equal(20f, state.Track.Points[1].Position.X, 0.001f);

        Assert.True(state.Undo());
        Assert.Equal(source, state.EditedTrackId);
        Assert.Equal(3, state.Scene.Tracks.Count);
        Assert.Equal(4, state.Track.Points.Count);
    }

    [Fact]
    public void MovingPointsToAHiddenTrackShowsIt()
    {
        var state = Editing();
        var hidden = Track(state, 1);
        state.SetTracksHidden([hidden], true);

        Assert.Null(state.MovePointsTo([0], hidden));

        Assert.Equal(hidden, state.EditedTrackId);
        Assert.DoesNotContain(hidden, state.Scene.Hidden);
        Assert.Equal([0], state.SelectedPoints);
    }

    [Fact]
    public void UndoingAMoveBringsBackTheSourceSelection()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(2, RowClick.Toggle);

        Assert.Null(state.MovePointsTo(state.SelectedPoints, Track(state, 2)));
        Assert.True(state.Undo());

        Assert.Equal([0, 2], state.SelectedPoints);
    }

    [Fact]
    public void AFollowTargetTrackTakesNoMovedPoints()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.SwitchTrack(Track(state, 1));
        state.SetAim(AimMode.FollowTarget, new ControlPoint(Vector3.Zero, 0f, 0f, 1f));
        state.SwitchTrack(first);

        Assert.Equal(TrackEditing.FollowHasOnePoint, state.MovePointsTo([0], Track(state, 1)));
        Assert.Equal(first, state.EditedTrackId);
    }

    [Fact]
    public void GoingLiveDropsGroupsButKeepsOnePoint()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.Cue();
        Assert.Equal([Track(state, 0)], state.SelectedTracks);

        state.Edit();
        state.Select(2);
        state.Cue();
        Assert.Equal([2], state.SelectedPoints);
    }

    [Fact]
    public void ReleasingDropsSeveralPoints()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        state.Release();

        Assert.Empty(state.SelectedPoints);
    }

    [Fact]
    public void DeletedTracksAndEntriesLeaveTheSelection()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.ClickTrack(Track(state, 2), RowClick.Toggle);
        var gone = Track(state, 2);

        Assert.Null(state.DeleteTracks([gone]));

        Assert.Equal([Track(state, 0), Track(state, 1)], state.SelectedTracks);
    }

    [Fact]
    public void RemovedEntriesLeaveTheSelection()
    {
        var state = Editing();
        state.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.ClickEntry(Entry(state, 1), RowClick.Toggle);

        Assert.Null(state.RemoveFromPlaylist([Entry(state, 0)]));

        Assert.Equal([Entry(state, 0)], state.SelectedEntries);
    }

    [Fact]
    public void SelectingAPointKeyCollapsesToThatPoint()
    {
        var state = Editing();
        state.Select(0);
        state.ClickPoint(1, RowClick.Toggle);

        state.SelectKey(TrackEditing.PointKey(state.StoredTrack, 3));

        Assert.Equal([3], state.SelectedPoints);
    }

    [Fact]
    public void SelectingAPointKeyClearsATrackSelection()
    {
        var state = Editing();
        state.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.SelectKey(TrackEditing.PointKey(state.StoredTrack, 0));

        Assert.Equal([0], state.SelectedPoints);
        Assert.Equal([Track(state, 0)], state.SelectedTracks);
    }

    [Fact]
    public void ShiftRangesFromTheLastClickedPointWhereItMovedTo()
    {
        var state = Editing();
        state.Select(3);

        // Point 4 moves to the top, so a Shift-click on 3 runs from the top down.
        Assert.Null(state.MovePoints([3], 3, 0));
        state.ClickPoint(2, RowClick.Range);

        Assert.Equal([0, 1, 2], state.SelectedPoints);
    }

    [Fact]
    public void ShiftRangesFromTheLastClickedPointAfterADeleteAboveIt()
    {
        var state = Editing();
        state.Select(2);

        // Point 1 goes, so the last clicked is now at 1, and Shift-clicking 0 selects 0 and 1.
        Assert.Null(state.DeletePoints([0]));
        state.ClickPoint(0, RowClick.Range);

        Assert.Equal([0, 1], state.SelectedPoints);
    }

    [Fact]
    public void ShiftRangesFromAPointAddedAfterTheSelectedOne()
    {
        var state = Editing();
        state.Select(1);

        Assert.Null(state.AddAfterSelected(Point(15f)));
        state.ClickPoint(3, RowClick.Range);

        Assert.Equal([2, 3], state.SelectedPoints);
    }

    [Fact]
    public void CtrlClickingTheLastOtherTrackOffRangesFromTheEditedOneAgain()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);
        state.ClickTrack(Track(state, 2), RowClick.Toggle);
        state.ClickTrack(Track(state, 2), RowClick.Toggle);

        state.ClickTrack(Track(state, 3), RowClick.Range);

        Assert.Equal(state.Scene.Tracks.Select(t => t.Id), state.SelectedTracks);
    }

    [Fact]
    public void UndoingAMoveOfAnUnselectedPointSelectsIt()
    {
        var state = Editing();
        state.Select(0);

        Assert.Null(state.MovePointsTo([2], Track(state, 1)));
        Assert.True(state.Undo());

        Assert.Equal([2], state.SelectedPoints);
    }
}
