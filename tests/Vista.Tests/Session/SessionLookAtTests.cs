using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
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
        state.BeginLiveEdit();
        Assert.Null(state.PreviewAimHeight(2f));
        state.EndLiveEdit();
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
        state.Selection.SelectLookAt(state.EditedTrackId);

        Assert.True(state.Undo());
        Assert.Null(state.Selection.Anchor);
        Assert.Null(state.Selection.LookAtInWorld);

        Assert.True(state.Redo());
        Assert.Equal(AimMode.LookAt, state.Track.Aim);
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void ALookAtDragBackToItsStartIsNoUndoStep()
    {
        var state = Looking();
        state.Selection.SelectLookAt(state.EditedTrackId);
        var start = state.Selection.LookAtInWorld!.Value;
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
        state.Selection.SelectLookAt(state.EditedTrackId);
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
        state.Selection.Select(1);
        Assert.NotNull(state.Selection.SelectLookAt(state.EditedTrackId));

        state.SetAim(AimMode.LookAt, Camera);

        Assert.Null(state.Selection.SelectLookAt(state.EditedTrackId));
        Assert.Equal(AnchorKind.LookAt, state.Selection.Anchor);
        Assert.Null(state.Selection.Point);
        Assert.Null(state.Selection.AnchorInWorld);
        Near(new Vector3(10f, 5f, -10f), state.Selection.LookAtInWorld!.Value, 1e-4f);
    }

    [Fact]
    public void TheLookAtPointIsSelectedOnlyWhileEditingOnATrackThatExists()
    {
        var state = Looking();
        Assert.Equal("There is no such track.", state.Selection.SelectLookAt(Guid.NewGuid()));

        state.Release();

        Assert.Equal(
            "The Look At point can only be selected while editing.",
            state.Selection.SelectLookAt(state.EditedTrackId)
        );
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void SelectingAnotherTracksLookAtSwitchesToIt()
    {
        var state = Looking();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

        Assert.Null(state.Selection.SelectLookAt(first));

        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.LookAt, state.Selection.Anchor);
    }

    [Fact]
    public void MovingTheLookAtLandsWhereItWasPutUnderTurnedAnchorsAsOneUndoStep()
    {
        var state = Looking();
        state.Selection.SelectSceneAnchor();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(100f, 1f, 20f), 0.7f), carry: true);
        state.EndLiveEdit();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(80f, 1f, 30f), -1.1f), carry: true);
        state.EndLiveEdit();
        state.Selection.SelectLookAt(state.EditedTrackId);
        var before = state.Selection.LookAtInWorld!.Value;
        var target = new Vector3(12f, 6f, 8f);

        state.BeginLiveEdit();
        Assert.Null(state.PreviewLookAt(target));
        state.EndLiveEdit();

        Near(target, state.Selection.LookAtInWorld!.Value, 1e-4f);
        Assert.True(state.Undo());
        Near(before, state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void ALookAtDragIsOneUndoStep()
    {
        var state = Looking();
        state.Selection.SelectLookAt(state.EditedTrackId);
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
        state.Selection.SelectTrackAnchor(state.EditedTrackId);

        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false);
        state.EndLiveEdit();
        Near(before, state.Track.LookAt, 1e-4f);

        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(6f, 0f, 9f), 1.3f), carry: true);
        state.EndLiveEdit();
        Near(before + new Vector3(10f, 0f, 0f), state.Track.LookAt, 1e-4f);
    }

    [Fact]
    public void LeavingLookAtDropsItsSelection()
    {
        var state = Looking();
        state.Selection.SelectLookAt(state.EditedTrackId);

        state.SetAim(AimMode.AimKeys, Camera);

        Assert.Null(state.Selection.Anchor);
        Assert.Null(state.Selection.LookAtInWorld);
    }

    [Fact]
    public void TheLookAtMovesOnlyWhenSelectedAndIsNoAnchor()
    {
        var state = Looking();
        state.BeginLiveEdit();
        Assert.NotNull(state.PreviewLookAt(Vector3.Zero));
        state.EndLiveEdit();

        state.Selection.SelectLookAt(state.EditedTrackId);

        state.BeginLiveEdit();
        Assert.NotNull(state.PreviewAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.EndLiveEdit();
    }

    [Fact]
    public void LookAheadIsAnUndoStepAndOnlyChangesWhileEditing()
    {
        var state = new SessionState();
        state.Edit();

        Assert.Null(state.SetLookAhead(1.5f));
        Assert.Equal(1.5f, state.Track.LookAhead);
        Assert.True(state.Undo());
        Assert.Equal(TrackEditing.DefaultLookAhead, state.Track.LookAhead);

        state.Release();
        Assert.NotNull(state.SetLookAhead(1f));
    }
}
