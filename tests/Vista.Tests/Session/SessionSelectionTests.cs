using System.Numerics;
using Vista.Core.Display;
using Vista.Core.Editing;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
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
        foreach (var x in new[] { 5f, 10f, 20f, 30f })
            state.AddToEnd(Point(x));
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
        state.Selection.Select(1);
        state.Selection.SelectLeg(2);

        state.Selection.ClickPoint(3, RowClick.Toggle);

        Assert.Equal([1, 3], state.Selection.Points);
        Assert.Null(state.Selection.Point);
        Assert.Null(state.Selection.Key);
        Assert.Null(state.Selection.Leg);
    }

    [Fact]
    public void ShiftClickingPointsRangesFromTheLastClicked()
    {
        var state = Editing();
        state.Selection.Select(0);

        state.Selection.ClickPoint(2, RowClick.Range);

        Assert.Equal([0, 1, 2], state.Selection.Points);
    }

    [Fact]
    public void TwoPointsBlockCtrlClickingATrackOrAnEntry()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        Assert.Null(state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle));
        Assert.Null(state.Selection.ClickEntry(Entry(state, 0), RowClick.Range));

        Assert.Equal([0, 1], state.Selection.Points);
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
        Assert.Empty(state.Selection.Entries);
    }

    [Fact]
    public void OnePointGivesWayToACtrlClickedTrack()
    {
        var state = Editing();
        state.Selection.Select(2);

        Assert.Null(state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle));

        Assert.Empty(state.Selection.Points);
        Assert.Equal([Track(state, 0), Track(state, 2)], state.Selection.Tracks);
        Assert.Equal(Track(state, 0), state.EditedTrackId);
    }

    [Fact]
    public void TwoTracksBlockCtrlClickingAPointOrAnEntry()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.Selection.ClickPoint(0, RowClick.Toggle);
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Toggle);

        Assert.Empty(state.Selection.Points);
        Assert.Empty(state.Selection.Entries);
        Assert.Equal([Track(state, 0), Track(state, 1)], state.Selection.Tracks);
    }

    [Fact]
    public void TwoEntriesBlockCtrlClickingAPointOrATrack()
    {
        var state = Editing();
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.Selection.ClickEntry(Entry(state, 2), RowClick.Toggle);

        state.Selection.ClickPoint(0, RowClick.Toggle);
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        Assert.Equal([Entry(state, 0), Entry(state, 2)], state.Selection.Entries);
        Assert.Empty(state.Selection.Points);
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
    }

    [Fact]
    public void CtrlClickingTheEditedTrackLeavesItSelected()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.Selection.ClickTrack(Track(state, 0), RowClick.Toggle);

        Assert.Equal([Track(state, 0), Track(state, 1)], state.Selection.Tracks);
    }

    [Fact]
    public void CtrlClickingTheEditedTrackKeepsASelectedPoint()
    {
        var state = Editing();
        state.Selection.Select(1);

        state.Selection.ClickTrack(Track(state, 0), RowClick.Toggle);

        Assert.Equal([1], state.Selection.Points);
    }

    [Fact]
    public void ShiftClickingAPointAfterCtrlClickingTheLastOneOffStartsAfresh()
    {
        var state = Editing();
        state.Selection.Select(1);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        state.Selection.ClickPoint(3, RowClick.Range);

        Assert.Equal([3], state.Selection.Points);
    }

    [Fact]
    public void ShiftClickingAnEntryAfterCtrlClickingTheLastOneOffStartsAfresh()
    {
        var state = Editing();
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Toggle);

        state.Selection.ClickEntry(Entry(state, 2), RowClick.Range);

        Assert.Equal([Entry(state, 2)], state.Selection.Entries);
    }

    [Fact]
    public void ShiftClickingATrackRangesFromTheEditedOneOnceTheTracksWereCleared()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.Selection.Select(0);

        state.Selection.ClickTrack(Track(state, 2), RowClick.Range);

        Assert.Equal([Track(state, 0), Track(state, 1), Track(state, 2)], state.Selection.Tracks);
    }

    [Fact]
    public void APlainClickOnATrackEditsItAndClearsTheRest()
    {
        var state = Editing();
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.Selection.ClickEntry(Entry(state, 1), RowClick.Toggle);

        Assert.Null(state.Selection.ClickTrack(Track(state, 1), RowClick.Plain));

        Assert.Equal(Track(state, 1), state.EditedTrackId);
        Assert.Equal([Track(state, 1)], state.Selection.Tracks);
        Assert.Empty(state.Selection.Entries);
    }

    [Fact]
    public void ClickingAPointPastTheEndChangesNothing()
    {
        var state = Editing();
        state.Selection.Select(1);

        // Four points: 4 is one past the last.
        state.Selection.ClickPoint(4, RowClick.Plain);

        Assert.Equal([1], state.Selection.Points);
    }

    [Fact]
    public void APlainClickOnAPointSelectsItOverTwoTracks()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.Selection.ClickPoint(2, RowClick.Plain);

        Assert.Equal([2], state.Selection.Points);
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
    }

    [Fact]
    public void APlainClickOnTheEditedTrackSelectsOnlyIt()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        Assert.Null(state.Selection.ClickTrack(Track(state, 0), RowClick.Plain));

        Assert.Equal(Track(state, 0), state.EditedTrackId);
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
    }

    [Fact]
    public void ClickingARowThatIsNotThereIsRefused()
    {
        var state = Editing();

        Assert.Equal("There is no such track.", state.Selection.ClickTrack(Guid.NewGuid(), RowClick.Plain));
        Assert.Equal("There is no such playlist entry.", state.Selection.ClickEntry(Guid.NewGuid(), RowClick.Plain));
    }

    [Fact]
    public void APlainClickOnAnEntryClearsThePoints()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        Assert.Null(state.Selection.ClickEntry(Entry(state, 1), RowClick.Plain));

        Assert.Empty(state.Selection.Points);
        Assert.Equal([Entry(state, 1)], state.Selection.Entries);
    }

    [Fact]
    public void SelectingNothingClearsEveryKind()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle);

        state.Selection.Select(null);

        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
    }

    [Fact]
    public void ARowClickClearsASelectedAnchor()
    {
        var state = Editing();
        Assert.Null(state.Selection.SelectSceneAnchor());

        state.Selection.ClickEntry(Entry(state, 0), RowClick.Toggle);

        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void ClicksOutsideEditChangeNothing()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Release();

        state.Selection.ClickPoint(1, RowClick.Toggle);

        Assert.Equal(
            "Tracks can only be selected while editing.",
            state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle)
        );
        Assert.Equal(
            "Playlist entries can only be selected while editing.",
            state.Selection.ClickEntry(Entry(state, 0), RowClick.Toggle)
        );
        state.Selection.SelectKey(2);
        state.Selection.SelectLeg(2);
        Assert.Equal([0], state.Selection.Points);
        Assert.Equal(0, state.Selection.Key);
        Assert.Null(state.Selection.Leg);
    }

    [Fact]
    public void DeletingTheSelectedPointsIsOneUndoStepThatBringsThemBack()
    {
        var state = Editing();
        state.Selection.Select(1);
        state.Selection.ClickPoint(3, RowClick.Toggle);

        Assert.Null(state.DeleteSelected());
        Assert.Equal(2, state.Track.Points.Count);
        Assert.Equal(5f, state.Track.Points[0].Position.X, 0.001f);
        Assert.Equal(20f, state.Track.Points[1].Position.X, 0.001f);
        Assert.Empty(state.Selection.Points);

        Assert.True(state.Undo());
        Assert.Equal(4, state.Track.Points.Count);
        Assert.Equal([1, 3], state.Selection.Points);
    }

    [Fact]
    public void DeletingSomePointsShiftsTheOthersStillSelected()
    {
        var state = Editing();
        state.Selection.Select(1);
        state.Selection.ClickPoint(3, RowClick.Toggle);

        // Point 0 goes: 1 and 3 become 0 and 2.
        Assert.Null(state.DeletePoints([0]));

        Assert.Equal([0, 2], state.Selection.Points);
    }

    [Fact]
    public void AddingAfterOrOverwritingNeedsOnePoint()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        Assert.Equal("Select one point first.", state.AddAfterSelected(Point(0f)));
        Assert.Equal("Select one point first.", state.OverwriteSelected(Point(0f)));
    }

    [Fact]
    public void MovingPointsAsABlockKeepsThemSelected()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        // 0 and 1 grabbed by 1 onto 3, below: after it, so 2 3 0 1.
        Assert.Null(state.MovePoints(state.Selection.Points, 1, 3));

        Assert.Equal(4, state.Track.Points.Count);
        Assert.Equal(20f, state.Track.Points[0].Position.X, 0.001f);
        Assert.Equal(30f, state.Track.Points[1].Position.X, 0.001f);
        Assert.Equal(5f, state.Track.Points[2].Position.X, 0.001f);
        Assert.Equal(10f, state.Track.Points[3].Position.X, 0.001f);
        Assert.Equal([2, 3], state.Selection.Points);
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
        Assert.Equal([0, 1], state.Selection.Points);
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
        Assert.Equal([0], state.Selection.Points);
    }

    [Fact]
    public void UndoingAMoveBringsBackTheSourceSelection()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(2, RowClick.Toggle);

        Assert.Null(state.MovePointsTo(state.Selection.Points, Track(state, 2)));
        Assert.True(state.Undo());

        Assert.Equal([0, 2], state.Selection.Points);
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
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.Cue();
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);

        state.Edit();
        state.Selection.Select(2);
        state.Cue();
        Assert.Equal([2], state.Selection.Points);
    }

    [Fact]
    public void ReleasingDropsSeveralPoints()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        state.Release();

        Assert.Empty(state.Selection.Points);
    }

    [Fact]
    public void DeletedTracksAndEntriesLeaveTheSelection()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);
        state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle);
        var gone = Track(state, 2);

        Assert.Null(state.DeleteTracks([gone]));

        Assert.Equal([Track(state, 0), Track(state, 1)], state.Selection.Tracks);
    }

    [Fact]
    public void RemovedEntriesLeaveTheSelection()
    {
        var state = Editing();
        state.Selection.ClickEntry(Entry(state, 0), RowClick.Plain);
        state.Selection.ClickEntry(Entry(state, 1), RowClick.Toggle);

        Assert.Null(state.RemoveFromPlaylist([Entry(state, 0)]));

        Assert.Equal([Entry(state, 0)], state.Selection.Entries);
    }

    [Fact]
    public void SelectingAPointKeyCollapsesToThatPoint()
    {
        var state = Editing();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);

        state.Selection.SelectKey(TrackEditing.PointKey(state.StoredTrack, 3));

        Assert.Equal([3], state.Selection.Points);
    }

    [Fact]
    public void SelectingAPointKeyClearsATrackSelection()
    {
        var state = Editing();
        state.Selection.ClickTrack(Track(state, 1), RowClick.Toggle);

        state.Selection.SelectKey(TrackEditing.PointKey(state.StoredTrack, 0));

        Assert.Equal([0], state.Selection.Points);
        Assert.Equal([Track(state, 0)], state.Selection.Tracks);
    }

    [Fact]
    public void ShiftRangesFromTheLastClickedPointWhereItMovedTo()
    {
        var state = Editing();
        state.Selection.Select(3);

        // Point 4 moves to the top, so a Shift-click on 3 runs from the top down.
        Assert.Null(state.MovePoints([3], 3, 0));
        state.Selection.ClickPoint(2, RowClick.Range);

        Assert.Equal([0, 1, 2], state.Selection.Points);
    }

    [Fact]
    public void ShiftRangesFromTheLastClickedPointAfterADeleteAboveIt()
    {
        var state = Editing();
        state.Selection.Select(2);

        // Point 1 goes, so the last clicked is now at 1, and Shift-clicking 0 selects 0 and 1.
        Assert.Null(state.DeletePoints([0]));
        state.Selection.ClickPoint(0, RowClick.Range);

        Assert.Equal([0, 1], state.Selection.Points);
    }

    [Fact]
    public void ShiftRangesFromAPointAddedAfterTheSelectedOne()
    {
        var state = Editing();
        state.Selection.Select(1);

        Assert.Null(state.AddAfterSelected(Point(15f)));
        state.Selection.ClickPoint(3, RowClick.Range);

        Assert.Equal([2, 3], state.Selection.Points);
    }

    [Fact]
    public void ShiftClickingATrackRangesFromTheLastCtrlClickedOne()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);
        state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle);

        state.Selection.ClickTrack(Track(state, 3), RowClick.Range);

        // The range runs from Track 3, the last clicked, not from the edited Track 1, so Track 2 stays out.
        Assert.Equal([Track(state, 0), Track(state, 2), Track(state, 3)], state.Selection.Tracks);
    }

    [Fact]
    public void ShiftRangesFromTheLastClickedPointAfterAnEditThatKeepsIt()
    {
        var state = Editing();
        state.Selection.Select(1);

        Assert.Null(state.AddToEnd(Point(40f)));
        state.Selection.ClickPoint(3, RowClick.Range);

        Assert.Equal([1, 2, 3], state.Selection.Points);
    }

    [Fact]
    public void AnEditThatLeavesTheLastClickedPointPastTheEndForgetsIt()
    {
        var state = Editing();
        state.Selection.Select(1);
        state.Selection.ClickPoint(3, RowClick.Toggle);
        state.Selection.ClickPoint(3, RowClick.Toggle);

        // Point 1 stays selected and 3 was clicked last; with 3 gone the track has three points, so 3 is past the end
        // and forgotten, and adding a fourth point doesn't bring it back: Shift-clicking 2 only adds 2.
        Assert.Null(state.ChangeTrack(t => TrackEditing.Delete(t, 3)));
        Assert.Null(state.AddToEnd(Point(40f)));
        state.Selection.ClickPoint(2, RowClick.Range);

        Assert.Equal([1, 2], state.Selection.Points);
    }

    [Fact]
    public void CtrlClickingTheLastOtherTrackOffRangesFromTheEditedOneAgain()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.SwitchTrack(first);
        state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle);
        state.Selection.ClickTrack(Track(state, 2), RowClick.Toggle);

        state.Selection.ClickTrack(Track(state, 3), RowClick.Range);

        Assert.Equal(state.Scene.Tracks.Select(t => t.Id), state.Selection.Tracks);
    }

    [Fact]
    public void UndoingAMoveOfAnUnselectedPointSelectsIt()
    {
        var state = Editing();
        state.Selection.Select(0);

        Assert.Null(state.MovePointsTo([2], Track(state, 1)));
        Assert.True(state.Undo());

        Assert.Equal([2], state.Selection.Points);
    }

    // Editing() with Track 2 given points at x = 0 and 10 too, so both tracks' anchors and the scene's are placed.
    private static SessionState TwoWithPoints()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.SwitchTrack(Track(state, 1));
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.SwitchTrack(first);
        return state;
    }

    // A plain click on another track's point edits that track and selects the point, as SelectPoint does.

    [Fact]
    public void APlainClickOnAnotherTracksPointEditsItAndSelectsThePoint()
    {
        var state = TwoWithPoints();

        Assert.Null(state.Selection.ClickMarker(new TrackMarker(Track(state, 1), 1, null), RowClick.Plain));

        Assert.Equal(Track(state, 1), state.EditedTrackId);
        Assert.Equal([1], state.Selection.Points);
    }

    [Fact]
    public void APlainClickOnTheEditedTracksPointSelectsOnlyIt()
    {
        var state = TwoWithPoints();
        state.Selection.Select(0);

        state.Selection.ClickMarker(new TrackMarker(Track(state, 0), 2, null), RowClick.Plain);

        Assert.Equal([2], state.Selection.Points);
    }

    // Anchors and the Look At point route to their own Select calls, so a Follow track's anchor is refused as SelectTrackAnchor refuses it.

    [Fact]
    public void APlainClickOnAnAnchorOrTheLookAtPointSelectsIt()
    {
        var state = TwoWithPoints();
        var first = Track(state, 0);

        state.Selection.ClickMarker(new TrackMarker(Guid.Empty, -1, null, MarkerKind.SceneAnchor), RowClick.Plain);
        Assert.Equal(AnchorKind.Scene, state.Selection.Anchor);

        state.Selection.ClickMarker(new TrackMarker(first, -1, null, MarkerKind.TrackAnchor), RowClick.Plain);
        Assert.Equal(AnchorKind.Track, state.Selection.Anchor);

        state.SetAim(AimMode.LookAt, Point(0f));
        state.Selection.ClickMarker(new TrackMarker(first, -1, null, MarkerKind.LookAt), RowClick.Plain);
        Assert.Equal(AnchorKind.LookAt, state.Selection.Anchor);

        state.ChangeTrack(t => t with { Aim = AimMode.FollowTarget });
        Assert.Equal(
            "A Follow Target track's anchor is hidden",
            state.Selection.ClickMarker(new TrackMarker(first, -1, null, MarkerKind.TrackAnchor), RowClick.Plain)
        );
    }

    [Fact]
    public void APlainClickOnNothingClearsTheSelection()
    {
        var state = TwoWithPoints();
        state.Selection.Select(1);

        state.Selection.ClickMarker(null, RowClick.Plain);

        Assert.Empty(state.Selection.Points);
    }

    // With Ctrl or Shift only the edited track's points respond; anything else leaves the selection and the edited track alone.

    [Fact]
    public void AModifiedClickActsOnlyOnTheEditedTracksPoints()
    {
        var state = TwoWithPoints();
        var first = Track(state, 0);
        state.Selection.Select(0);

        state.Selection.ClickMarker(new TrackMarker(first, 1, null), RowClick.Toggle);
        Assert.Equal([0, 1], state.Selection.Points);

        Assert.Null(state.Selection.ClickMarker(new TrackMarker(Track(state, 1), 0, null), RowClick.Toggle));
        state.Selection.ClickMarker(null, RowClick.Range);

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal([0, 1], state.Selection.Points);
    }
}
