using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionAnchorTests
{
    [Fact]
    public void TheFirstPointPlacesBothAnchorsOnTheGroundUnderIt()
    {
        var state = EditingOverGround();

        Assert.True(state.Scene.AnchorPlaced);
        Assert.Equal(new Anchor(new Vector3(10f, 1f, 0f), 0f), state.Scene.Anchor);
        Assert.True(state.Scene.Tracks[0].AnchorPlaced);
        Assert.Equal(Anchor.Origin, state.Scene.Tracks[0].Anchor);
        Assert.Equal(new Vector3(0f, 4f, 0f), state.Scene.Tracks[0].Points[0].Position);
        Near(new Vector3(20f, 5f, 0f), state.Track.Points[1].Position, 1e-4f);
    }

    [Fact]
    public void ALaterTracksAnchorIsPlacedUnderItsOwnFirstPoint()
    {
        var state = EditingOverGround();
        state.AddTrack();
        state.AddToEnd(HeadHeightPoint(50f, 7f, 5f));

        Assert.Equal(new Vector3(10f, 1f, 0f), state.Scene.Anchor.Position);
        Near(new Vector3(50f, 1f, 5f), SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[1]).Position, 1e-4f);
        Near(new Vector3(50f, 7f, 5f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void WorldOfKeepsTheSameInstanceUntilSomethingChanges()
    {
        var state = EditingOverGround();
        Assert.Same(state.Track, state.Track);
        Assert.Same(state.World.WorldOf(state.Scene.Tracks[0]), state.Track);
    }

    [Fact]
    public void SelectingAnAnchorClearsThePointAndSelectingAPointClearsTheAnchor()
    {
        var state = EditingOverGround();
        state.Selection.Select(1);

        Assert.Null(state.Selection.SelectSceneAnchor());
        Assert.Equal(AnchorKind.Scene, state.Selection.Anchor);
        Assert.Null(state.Selection.Point);

        state.Selection.Select(2);
        Assert.Null(state.Selection.Anchor);

        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.Selection.Select(null);
        Assert.Null(state.Selection.Anchor);
        Assert.Null(state.Selection.Point);
    }

    [Fact]
    public void SelectingAnotherTracksAnchorSwitchesToIt()
    {
        var state = EditingOverGround();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(HeadHeightPoint(40f));

        Assert.Null(state.Selection.SelectTrackAnchor(first));
        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.Track, state.Selection.Anchor);
    }

    [Fact]
    public void AnUnplacedAnchorCannotBeSelected()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.Selection.SelectSceneAnchor());
        Assert.NotNull(state.Selection.SelectTrackAnchor(state.EditedTrackId));
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void AnchorsAreSelectedOnlyWhileEditingOnATrackThatExists()
    {
        var state = EditingOverGround();
        Assert.Equal("There is no such track.", state.Selection.SelectTrackAnchor(Guid.NewGuid()));

        state.Release();

        Assert.Equal("Anchors can only be selected while editing.", state.Selection.SelectSceneAnchor());
        Assert.Equal(
            "Anchors can only be selected while editing.",
            state.Selection.SelectTrackAnchor(state.EditedTrackId)
        );
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void SelectingAnAnchorEndsALiveEdit()
    {
        var state = EditingOverGround();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewPoint(1, HeadHeightPoint(25f)));

        Assert.Null(state.Selection.SelectSceneAnchor());

        Assert.Equal(
            "No live edit is in progress.",
            state.PreviewAnchor(new Anchor(new Vector3(12f, 1f, 0f), 0f), carry: true)
        );
        Near(new Vector3(25f, 5f, 0f), state.Track.Points[1].Position, 1e-4f);
    }

    [Fact]
    public void UndoingAnAnchorDragKeepsTheAnchorSelected()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(12f, 1f, 0f), 0f), carry: true);
        state.EndLiveEdit();

        Assert.True(state.Undo());

        Assert.Equal(AnchorKind.Scene, state.Selection.Anchor);
    }

    [Fact]
    public void MovingTheSceneAnchorCarriesThePointsAsOneUndoStep()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var before = state.Track.Points[2].Position;

        state.BeginLiveEdit();
        Assert.Null(state.PreviewAnchor(new Anchor(new Vector3(15f, 1f, 3f), 0f), carry: true));
        state.EndLiveEdit();

        Near(before + new Vector3(5f, 0f, 3f), state.Track.Points[2].Position, 1e-4f);
        Assert.True(state.Undo());
        Near(before, state.Track.Points[2].Position, 1e-4f);
    }

    [Fact]
    public void MovingAnAnchorAloneLeavesThePointsInTheWorld()
    {
        var state = EditingOverGround();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        var before = state.Track.Points.Select(p => p.Position).ToList();

        state.BeginLiveEdit();
        Assert.Null(state.PreviewAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false));
        state.EndLiveEdit();

        for (var i = 0; i < before.Count; i++)
            Near(before[i], state.Track.Points[i].Position, 1e-4f);
        Near(new Vector3(-4f, 0f, 9f), state.Selection.AnchorInWorld!.Value.Position, 1e-4f);
    }

    [Fact]
    public void APointReplacedInTheWorldLandsWhereItWasPut()
    {
        var state = EditingOverGround();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(3f, 0f, -2f), 0.9f), carry: true);
        state.EndLiveEdit();
        var target = new ControlPoint(new Vector3(12f, 6f, 8f), 0.4f, 0.1f, 1f);

        Assert.Null(state.ReplacePoint(1, target));

        Near(target.Position, state.Track.Points[1].Position, 1e-4f);
        Assert.Equal(0.4f, state.Track.Points[1].Yaw, 1e-4f);
    }

    [Fact]
    public void AnAnchorDragIsOneUndoStepAndADragBackIsNone()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var start = state.Scene.Anchor;

        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(12f, 1f, 0f), 0f), carry: true);
        state.PreviewAnchor(new Anchor(new Vector3(14f, 1f, 0f), 0f), carry: true);
        state.EndLiveEdit();
        Assert.Equal(new Vector3(14f, 1f, 0f), state.Scene.Anchor.Position);
        Assert.True(state.Undo());
        Assert.Equal(start, state.Scene.Anchor);

        state.Redo();
        state.Undo();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(40f, 1f, 0f), 0f), carry: false);
        state.PreviewAnchor(start, carry: false);
        state.EndLiveEdit();

        // No step was recorded: recording one would have cleared the redo left by the Undo above.
        Assert.True(state.CanRedo);
        Assert.Equal(start, state.Scene.Anchor);
    }

    [Fact]
    public void TheGroundIsReadUnderTheFirstPoint()
    {
        Vector3? asked = null;
        var state = new SessionState(p =>
        {
            asked = p;
            return 2f;
        });
        state.Edit();

        state.AddToEnd(HeadHeightPoint(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), asked);
        Assert.Equal(2f, state.Scene.Anchor.Position.Y);
        Assert.Equal(2f, SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[0]).Position.Y);
    }

    [Fact]
    public void WithNoGroundTheAnchorsSitAtThePointsHeight()
    {
        var state = new SessionState(_ => null);
        state.Edit();

        state.AddToEnd(HeadHeightPoint(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), state.Scene.Anchor.Position);
    }

    [Fact]
    public void TheGroundIsNotReadOnceBothAnchorsArePlaced()
    {
        var reads = 0;
        var state = new SessionState(_ =>
        {
            reads++;
            return 1f;
        });
        state.Edit();
        state.AddToEnd(HeadHeightPoint(10f));
        Assert.Equal(1, reads);

        state.AddToEnd(HeadHeightPoint(20f));
        state.Selection.Select(0);
        state.AddAfterSelected(HeadHeightPoint(15f));

        Assert.Equal(1, reads);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void ClearKeepsTheAnchorSoTheNextPointIsNotReplaced()
    {
        var state = EditingOverGround();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(0f, 0f, 0f), 0f), carry: false);
        state.EndLiveEdit();
        var anchor = state.Scene.Tracks[0].Anchor;

        state.ChangeTrack(TrackEditing.Clear);
        state.AddToEnd(HeadHeightPoint(90f));

        Assert.Equal(anchor, state.Scene.Tracks[0].Anchor);
        Near(new Vector3(90f, 5f, 0f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void ADuplicateSitsOnTheOriginal()
    {
        var state = EditingOverGround();
        state.DuplicateTrack(state.EditedTrackId);

        for (var i = 0; i < 3; i++)
            Near(state.World.WorldOf(state.Scene.Tracks[0]).Points[i].Position, state.Track.Points[i].Position, 1e-4f);
    }

    [Fact]
    public void UndoingTheFirstPointDropsTheSceneAnchorSelection()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.AddToEnd(HeadHeightPoint(10f));
        state.Selection.SelectSceneAnchor();

        Assert.True(state.Undo());

        Assert.False(state.Scene.AnchorPlaced);
        Assert.Null(state.Selection.Anchor);
        state.BeginLiveEdit();
        Assert.NotNull(state.PreviewAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        state.EndLiveEdit();
    }

    [Fact]
    public void UndoingALaterTracksFirstPointDropsItsAnchorSelection()
    {
        var state = EditingOverGround();
        state.AddTrack();
        state.AddToEnd(HeadHeightPoint(40f));
        state.Selection.SelectTrackAnchor(state.EditedTrackId);

        Assert.True(state.Undo());

        Assert.False(state.Scene.Tracks[1].AnchorPlaced);
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void MovingAnAnchorToWhereItIsRecordsNoStep()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();

        state.BeginLiveEdit();
        Assert.Null(state.PreviewAnchor(state.Selection.AnchorInWorld!.Value, carry: true));
        state.EndLiveEdit();

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void DeletingAnotherTrackLeavesTheEditedTrackWhereItIs()
    {
        var state = EditingOverGround();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(HeadHeightPoint(40f));
        var second = state.EditedTrackId;
        state.SwitchTrack(first);
        state.Selection.SelectSceneAnchor();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(-7f, 2f, 11f), 0.8f), carry: true);
        state.EndLiveEdit();
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.DeleteTracks([second]));

        Assert.True(state.Scene.AnchorPlaced);
        for (var i = 0; i < before.Count; i++)
            Near(before[i], state.Track.Points[i].Position, 1e-4f);
    }

    // EditingOverGround() with both anchors moved and turned by different, non-zero yaws.
    private static SessionState EditingWithTurnedAnchors()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(100f, 1f, 20f), 0.7f), carry: true);
        state.EndLiveEdit();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(80f, 1f, 30f), -1.1f), carry: true);
        state.EndLiveEdit();
        return state;
    }

    [Fact]
    public void AddingToTheEndLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        var target = new ControlPoint(new Vector3(123f, 4f, -56f), 1.234f, 0f, 1f);

        Assert.Null(state.AddToEnd(target));

        Near(target.Position, state.Track.Points[^1].Position, 1e-4f);
        Assert.Equal(target.Yaw, state.Track.Points[^1].Yaw, 1e-4f);
    }

    [Fact]
    public void AddingAfterSelectedLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        state.Selection.Select(1);
        var target = new ControlPoint(new Vector3(-30f, 2f, 77f), -0.4f, 0f, 1f);

        Assert.Null(state.AddAfterSelected(target));

        Near(target.Position, state.Track.Points[2].Position, 1e-4f);
        Assert.Equal(target.Yaw, state.Track.Points[2].Yaw, 1e-4f);
    }

    [Fact]
    public void ALivePreviewLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        var target = new ControlPoint(new Vector3(15f, 9f, -3f), 2.5f, 0f, 1f);

        state.BeginLiveEdit();
        Assert.Null(state.PreviewPoint(0, target));
        Near(target.Position, state.Track.Points[0].Position, 1e-4f);
        Assert.Equal(target.Yaw, state.Track.Points[0].Yaw, 1e-4f);
        state.EndLiveEdit();
    }

    [Fact]
    public void MovingTheSceneAnchorToThePlayerCarriesItsPoints()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var before = state.Track.Points[2].Position;

        Assert.Null(state.MoveAnchorTo(new Vector3(50f, 2f, 7f), carry: true));

        // The scene anchor was (10, 1, 0), yaw 0; moving it to (50, 2, 7) keeps yaw 0
        // and carries every point by the offset (50 - 10, 2 - 1, 7 - 0) = (40, 1, 7).
        Assert.Equal(new Anchor(new Vector3(50f, 2f, 7f), 0f), state.Scene.Anchor);
        Near(before + new Vector3(40f, 1f, 7f), state.Track.Points[2].Position, 1e-4f);
    }

    [Fact]
    public void MovingTheSceneAnchorToThePlayerAloneLeavesPointsInPlace()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.MoveAnchorTo(new Vector3(50f, 2f, 7f), carry: false));

        Assert.Equal(new Anchor(new Vector3(50f, 2f, 7f), 0f), state.Scene.Anchor);
        for (var i = 0; i < before.Count; i++)
            Near(before[i], state.Track.Points[i].Position, 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorToThePlayerCarriesItsPoints()
    {
        var state = EditingOverGround();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        var before = state.Track.Points[2].Position;

        Assert.Null(state.MoveAnchorTo(new Vector3(-4f, 0f, 9f), carry: true));

        // The track anchor's world position was (10, 1, 0), yaw 0; moving it to (-4, 0, 9) keeps yaw 0
        // and carries every point by the offset (-4 - 10, 0 - 1, 9 - 0) = (-14, -1, 9).
        var moved = SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[0]);
        Assert.Equal(new Anchor(new Vector3(-4f, 0f, 9f), 0f), moved);
        Near(before + new Vector3(-14f, -1f, 9f), state.Track.Points[2].Position, 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorToThePlayerAloneLeavesItsPointsInPlace()
    {
        var state = EditingOverGround();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.MoveAnchorTo(new Vector3(-4f, 0f, 9f), carry: false));

        var moved = SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[0]);
        Assert.Equal(new Anchor(new Vector3(-4f, 0f, 9f), 0f), moved);
        for (var i = 0; i < before.Count; i++)
            Near(before[i], state.Track.Points[i].Position, 1e-4f);
    }

    [Fact]
    public void MovingATrackAnchorToThePlayerLeavesOtherTracksWhereTheyAre()
    {
        var state = EditingOverGround();
        state.AddTrack();
        state.AddToEnd(HeadHeightPoint(40f));
        var second = state.EditedTrackId;
        state.SwitchTrack(TrackId(state, 0));
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        var otherBefore = SceneGeometry.WorldAnchor(state.Scene, SceneEditing.Get(state.Scene, second));

        Assert.Null(state.MoveAnchorTo(new Vector3(-4f, 0f, 9f), carry: true));

        Assert.Equal(otherBefore, SceneGeometry.WorldAnchor(state.Scene, SceneEditing.Get(state.Scene, second)));
    }

    [Fact]
    public void UndoingAMoveToThePlayerPutsTheAnchorAndPointsBackExactly()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var anchorBefore = state.Scene.Anchor;
        var pointsBefore = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.MoveAnchorTo(new Vector3(50f, 2f, 7f), carry: true));

        Assert.True(state.Undo());

        Assert.Equal(anchorBefore, state.Scene.Anchor);
        for (var i = 0; i < pointsBefore.Count; i++)
            Near(pointsBefore[i], state.Track.Points[i].Position, 1e-4f);
    }

    [Fact]
    public void MovingTheAnchorToWhereItIsRecordsNoStep()
    {
        // EditingOverGround() already has one undo step per point added; undoing after a no-op move
        // removes the last point rather than reversing the move, proving the move recorded no step of its own.
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();

        Assert.Null(state.MoveAnchorTo(state.Scene.Anchor.Position, carry: true));

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void MovingTheAnchorIsRefusedWithNothingSelected()
    {
        var state = EditingOverGround();
        var before = state.Scene.Anchor;

        Assert.NotNull(state.MoveAnchorTo(new Vector3(1f, 1f, 1f), carry: true));

        Assert.Equal(before, state.Scene.Anchor);
    }

    [Fact]
    public void MovingTheAnchorIsRefusedWithTheLookAtPointSelected()
    {
        var state = EditingOverGround();
        state.SetAim(AimMode.LookAt, new ControlPoint(new Vector3(0f, 5f, 0f), 0f, 0f, 1f));
        state.Selection.SelectLookAt(state.EditedTrackId);
        var before = state.Scene.Anchor;

        Assert.NotNull(state.MoveAnchorTo(new Vector3(1f, 1f, 1f), carry: true));

        Assert.Equal(before, state.Scene.Anchor);
    }

    [Fact]
    public void MovingTheAnchorIsRefusedOutsideEdit()
    {
        var state = EditingOverGround();
        state.Selection.SelectSceneAnchor();
        var before = state.Scene.Anchor;
        state.Release();

        Assert.NotNull(state.MoveAnchorTo(new Vector3(1f, 1f, 1f), carry: true));

        Assert.Equal(before, state.Scene.Anchor);
    }

    [Fact]
    public void CanMoveAnchorIsTrueOnlyInEditWithTheSceneOrATrackAnchorSelected()
    {
        var state = EditingOverGround();
        Assert.False(state.CanMoveAnchor);

        state.Selection.SelectSceneAnchor();
        Assert.True(state.CanMoveAnchor);

        state.Release(CameraMode.View);
        Assert.False(state.CanMoveAnchor);

        state.Edit();
        Assert.True(state.CanMoveAnchor);

        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        Assert.True(state.CanMoveAnchor);

        state.Selection.Select(0);
        Assert.False(state.CanMoveAnchor);

        state.SetAim(AimMode.LookAt, new ControlPoint(new Vector3(0f, 5f, 0f), 0f, 0f, 1f));
        state.Selection.SelectLookAt(state.EditedTrackId);
        Assert.False(state.CanMoveAnchor);
    }
}
