using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionLookAtTests
{
    private static readonly ControlPoint Camera = new(new Vector3(0f, 5f, 0f), 0f, 0f, 1f);

    // These tests put every point at head height, so y defaults to 5 here.
    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => Fixtures.Point(x, y, z);

    // Editing with the ground at y = 1; Track 1 has points at x = 10, 20, 30 (y = 5), all aimed along −z.
    private static SessionState Editing()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddToEnd(Point(30f));
        return state;
    }

    // Editing() set to Look At; its point sits 10 yalms along the first point's aim, at (10, 5, −10).
    private static SessionState Looking()
    {
        var state = Editing();
        state.SetAim(AimMode.LookAt, Camera);
        return state;
    }

    [Fact]
    public void ChoosingLookAtPlacesThePointAlongTheFirstPointsAimAsOneUndoStep()
    {
        var state = Editing();

        Assert.Null(state.SetAim(AimMode.LookAt, Camera));

        Near(new Vector3(10f, 5f, -10f), state.Track.LookAt, 1e-4f);
        Assert.True(state.Undo());
        Assert.Equal(AimMode.AimKeys, state.Track.Aim);
        Assert.False(state.Track.LookAtPlaced);
    }

    [Fact]
    public void WithNoPointsItGoesAheadOfTheCameraAndStaysThereWhenTheFirstPointIsAdded()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.SetAim(AimMode.LookAt, new ControlPoint(new Vector3(50f, 5f, 50f), 0f, 0f, 1f));
        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt, 1e-4f);

        state.AddToEnd(Point(10f));

        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void TheWatchSettingsAreEachOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetAim(AimMode.WatchTarget, Camera));
        Assert.Null(state.SetTarget("Guard", null));
        Assert.Null(state.SetAimHeight(2f));
        Assert.Null(state.SetSmoothing(0.8f));
        Assert.Equal(0.8f, state.Track.Smoothing);

        state.Undo();
        Assert.Equal(0.3f, state.Track.Smoothing);
        Assert.Equal(2f, state.Track.AimHeight);
        state.Undo();
        Assert.Equal(1.3f, state.Track.AimHeight);
        state.Undo();
        Assert.Null(state.Track.TargetName);
        state.Undo();
        Assert.Equal(AimMode.AimKeys, state.Track.Aim);
    }

    [Fact]
    public void ChoosingAPlayerIsOneUndoStepForNameAndWorld()
    {
        var state = Editing();
        Assert.Null(state.SetTarget("Aya", "Gilgamesh"));
        Assert.Null(state.SetTarget("Aya", "Cactuar"));
        Assert.Equal("Cactuar", state.Track.TargetWorld);

        state.Undo();
        Assert.Equal("Aya", state.Track.TargetName);
        Assert.Equal("Gilgamesh", state.Track.TargetWorld);
        state.Undo();
        Assert.Null(state.Track.TargetName);
        Assert.Null(state.Track.TargetWorld);
    }

    [Fact]
    public void ASettingThatChangesNothingIsNoUndoStep()
    {
        var state = new SessionState();
        state.Edit();

        Assert.Null(state.SetSmoothing(0.3f));

        Assert.False(state.CanUndo);
    }

    [Fact]
    public void UndoingLookAtDropsItsSelectionAndRedoLeavesItDropped()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);

        Assert.True(state.Undo());
        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.SelectedLookAtInWorld);

        Assert.True(state.Redo());
        Assert.Equal(AimMode.LookAt, state.Track.Aim);
        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void ALookAtDragBackToItsStartIsNoUndoStep()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);
        var start = state.SelectedLookAtInWorld!.Value;
        var couldUndo = state.CanUndo;

        state.BeginLiveEdit();
        state.PreviewLookAt(new Vector3(3f, 5f, 0f));
        state.PreviewLookAt(start);
        state.EndLiveEdit();

        Assert.Equal(couldUndo, state.CanUndo);
        Assert.True(state.Undo());
        Assert.Equal(AimMode.AimKeys, state.Track.Aim);
    }

    [Fact]
    public void UndoDuringALookAtDragPutsThePointBack()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);
        var start = state.Track.LookAt;

        state.BeginLiveEdit();
        state.PreviewLookAt(new Vector3(3f, 5f, 0f));
        Assert.True(state.Undo());
        Assert.NotNull(state.PreviewLookAt(new Vector3(6f, 5f, 0f)));
        state.EndLiveEdit();

        Near(start, state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void TheLookAtPointCanBeSelectedOnlyUnderLookAt()
    {
        var state = Editing();
        state.Select(1);
        Assert.NotNull(state.SelectLookAt(state.EditedTrackId));

        state.SetAim(AimMode.LookAt, Camera);

        Assert.Null(state.SelectLookAt(state.EditedTrackId));
        Assert.Equal(AnchorKind.LookAt, state.SelectedAnchor);
        Assert.Null(state.Selected);
        Assert.Null(state.SelectedAnchorInWorld);
        Near(new Vector3(10f, 5f, -10f), state.SelectedLookAtInWorld!.Value, 1e-4f);
    }

    [Fact]
    public void SelectingAnotherTracksLookAtSwitchesToIt()
    {
        var state = Looking();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

        Assert.Null(state.SelectLookAt(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.LookAt, state.SelectedAnchor);
    }

    [Fact]
    public void MovingTheLookAtLandsWhereItWasPutUnderTurnedAnchorsAsOneUndoStep()
    {
        var state = Looking();
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(100f, 1f, 20f), 0.7f), carry: true);
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(80f, 1f, 30f), -1.1f), carry: true);
        state.SelectLookAt(state.EditedTrackId);
        var before = state.SelectedLookAtInWorld!.Value;
        var target = new Vector3(12f, 6f, 8f);

        Assert.Null(state.MoveLookAt(target));

        Near(target, state.SelectedLookAtInWorld!.Value, 1e-4f);
        Assert.True(state.Undo());
        Near(before, state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void ALookAtDragIsOneUndoStep()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);
        var start = state.Track.LookAt;

        state.BeginLiveEdit();
        Assert.Null(state.PreviewLookAt(new Vector3(0f, 5f, 0f)));
        Assert.Null(state.PreviewLookAt(new Vector3(3f, 5f, 0f)));
        state.EndLiveEdit();

        Near(new Vector3(3f, 5f, 0f), state.Track.LookAt, 1e-4f);
        Assert.True(state.Undo());
        Near(start, state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void TheTrackAnchorCarriesTheLookAtAndAloneLeavesItInTheWorld()
    {
        var state = Looking();
        var before = state.Track.LookAt;
        state.SelectTrackAnchor(state.EditedTrackId);

        state.MoveAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false);
        Near(before, state.Track.LookAt, 1e-4f);

        state.MoveAnchor(new Anchor(new Vector3(6f, 0f, 9f), 1.3f), carry: true);
        Near(before + new Vector3(10f, 0f, 0f), state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void LeavingLookAtDropsItsSelection()
    {
        var state = Looking();
        state.SelectLookAt(state.EditedTrackId);

        state.SetAim(AimMode.AimKeys, Camera);

        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.SelectedLookAtInWorld);
    }

    [Fact]
    public void TheLookAtMovesOnlyWhenSelectedAndIsNoAnchor()
    {
        var state = Looking();
        Assert.NotNull(state.MoveLookAt(Vector3.Zero));

        state.SelectLookAt(state.EditedTrackId);

        Assert.NotNull(state.MoveAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.BeginLiveEdit();
        Assert.NotNull(state.PreviewAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.EndLiveEdit();
    }
}
