using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionLookAtTests
{
    private const float Tolerance = 1e-4f;

    private static readonly ControlPoint Camera = new(new Vector3(0f, 5f, 0f), 0f, 0f, 1f);

    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

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

        Near(new Vector3(10f, 5f, -10f), state.Track.LookAt);
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
        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt);

        state.AddToEnd(Point(10f));

        Near(new Vector3(50f, 5f, 40f), state.Track.LookAt);
    }

    [Fact]
    public void TheFollowSettingsAreEachOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetAim(AimMode.FollowTarget, Camera));
        Assert.Null(state.SetTarget("Guard"));
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
    public void TheSettingsAreRefusedUnlessEditing()
    {
        var state = new SessionState();

        Assert.NotNull(state.SetAim(AimMode.LookAt, Camera));
        Assert.NotNull(state.SetTarget("Guard"));
        Assert.NotNull(state.SelectLookAt(state.EditedTrackId));
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
        Near(new Vector3(10f, 5f, -10f), state.SelectedLookAtInWorld!.Value);
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

        Near(target, state.SelectedLookAtInWorld!.Value);
        Assert.True(state.Undo());
        Near(before, state.Track.LookAt);
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

        Near(new Vector3(3f, 5f, 0f), state.Track.LookAt);
        Assert.True(state.Undo());
        Near(start, state.Track.LookAt);
    }

    [Fact]
    public void TheTrackAnchorCarriesTheLookAtAndAloneLeavesItInTheWorld()
    {
        var state = Looking();
        var before = state.Track.LookAt;
        state.SelectTrackAnchor(state.EditedTrackId);

        state.MoveAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false);
        Near(before, state.Track.LookAt);

        state.MoveAnchor(new Anchor(new Vector3(6f, 0f, 9f), 1.3f), carry: true);
        Near(before + new Vector3(10f, 0f, 0f), state.Track.LookAt);
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
