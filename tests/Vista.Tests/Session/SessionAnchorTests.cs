using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionAnchorTests
{
    private const float Tolerance = 1e-4f;

    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => new(new Vector3(x, y, z), 0f, 0f, 1f);

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    // Editing with the character's feet at y = 1; Track 1 has points at x = 10, 20, 30 (y = 5).
    private static SessionState Editing()
    {
        var state = new SessionState(() => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddToEnd(Point(30f));
        return state;
    }

    [Fact]
    public void TheFirstPointPlacesBothAnchorsUnderItAtFootHeight()
    {
        var state = Editing();

        Assert.True(state.Scene.AnchorPlaced);
        Assert.Equal(new Anchor(new Vector3(10f, 1f, 0f), 0f), state.Scene.Anchor);
        Assert.True(state.Scene.Tracks[0].AnchorPlaced);
        Assert.Equal(Anchor.Origin, state.Scene.Tracks[0].Anchor);
        Assert.Equal(new Vector3(0f, 4f, 0f), state.Scene.Tracks[0].Points[0].Position);
        Near(new Vector3(20f, 5f, 0f), state.Track.Points[1].Position);
    }

    [Fact]
    public void WithoutAFootHeightAnchorsSitAtThePointsHeight()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), state.Scene.Anchor.Position);
    }

    [Fact]
    public void ALaterTracksAnchorIsPlacedUnderItsOwnFirstPoint()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(50f, 7f, 5f));

        Assert.Equal(new Vector3(10f, 1f, 0f), state.Scene.Anchor.Position);
        Near(new Vector3(50f, 1f, 5f), SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[1]).Position);
        Near(new Vector3(50f, 7f, 5f), state.Track.Points[0].Position);
    }

    [Fact]
    public void WorldOfKeepsTheSameInstanceUntilSomethingChanges()
    {
        var state = Editing();
        Assert.Same(state.Track, state.Track);
        Assert.Same(state.WorldOf(state.Scene.Tracks[0]), state.Track);
    }

    [Fact]
    public void SelectingAnAnchorClearsThePointAndSelectingAPointClearsTheAnchor()
    {
        var state = Editing();
        state.Select(1);

        Assert.Null(state.SelectSceneAnchor());
        Assert.Equal(AnchorKind.Scene, state.SelectedAnchor);
        Assert.Null(state.Selected);

        state.Select(2);
        Assert.Null(state.SelectedAnchor);

        state.SelectTrackAnchor(state.EditedTrackId);
        state.Select(null);
        Assert.Null(state.SelectedAnchor);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void SelectingAnotherTracksAnchorSwitchesToIt()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

        Assert.Null(state.SelectTrackAnchor(first));
        Assert.Equal(first, state.EditedTrackId);
        Assert.Equal(AnchorKind.Track, state.SelectedAnchor);
    }

    [Fact]
    public void AnUnplacedAnchorCannotBeSelected()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.SelectSceneAnchor());
        Assert.NotNull(state.SelectTrackAnchor(state.EditedTrackId));
        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void MovingTheSceneAnchorCarriesThePointsAsOneUndoStep()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        var before = state.Track.Points[2].Position;

        Assert.Null(state.MoveAnchor(new Anchor(new Vector3(15f, 1f, 3f), 0f), carry: true));

        Near(before + new Vector3(5f, 0f, 3f), state.Track.Points[2].Position);
        Assert.True(state.Undo());
        Near(before, state.Track.Points[2].Position);
    }

    [Fact]
    public void MovingAnAnchorAloneLeavesThePointsInTheWorld()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.MoveAnchor(new Anchor(new Vector3(-4f, 0f, 9f), 1.3f), carry: false));

        for (var i = 0; i < before.Count; i++) Near(before[i], state.Track.Points[i].Position);
        Near(new Vector3(-4f, 0f, 9f), state.SelectedAnchorInWorld!.Value.Position);
    }

    [Fact]
    public void TimingIsUnchangedByTurningTheScene()
    {
        var state = Editing();
        var duration = state.Duration;
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(-200f, 30f, 90f), 2.2f), carry: true);

        Assert.Equal(duration, state.Duration, 4);
    }

    [Fact]
    public void APointReplacedInTheWorldLandsWhereItWasPut()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(3f, 0f, -2f), 0.9f), carry: true);
        var target = new ControlPoint(new Vector3(12f, 6f, 8f), 0.4f, 0.1f, 1f);

        Assert.Null(state.ReplacePoint(1, target));

        Near(target.Position, state.Track.Points[1].Position);
        Assert.Equal(0.4f, state.Track.Points[1].Yaw, Tolerance);
    }

    [Fact]
    public void AnAnchorDragIsOneUndoStepAndADragBackIsNone()
    {
        var state = Editing();
        state.SelectSceneAnchor();
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
    public void BringSceneMovesTheSceneAnchorToTheCameraAtFootHeightKeepingItsYaw()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(state.Scene.Anchor.Position, 0.6f), carry: true);
        var offset = state.Track.Points[0].Position - state.Scene.Anchor.Position;

        Assert.Null(state.BringScene(new Vector3(-50f, 20f, 70f)));

        Assert.Equal(new Anchor(new Vector3(-50f, 1f, 70f), 0.6f), state.Scene.Anchor);
        Near(new Vector3(-50f, 1f, 70f) + offset, state.Track.Points[0].Position);
        Assert.True(state.Undo());
    }

    [Fact]
    public void ClearKeepsTheAnchorSoTheNextPointIsNotReplaced()
    {
        var state = Editing();
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(0f, 0f, 0f), 0f), carry: false);
        var anchor = state.Scene.Tracks[0].Anchor;

        state.ChangeTrack(TrackEditing.Clear);
        state.AddToEnd(Point(90f));

        Assert.Equal(anchor, state.Scene.Tracks[0].Anchor);
        Near(new Vector3(90f, 5f, 0f), state.Track.Points[0].Position);
    }

    [Fact]
    public void ADuplicateSitsOnTheOriginal()
    {
        var state = Editing();
        state.DuplicateTrack(state.EditedTrackId);

        for (var i = 0; i < 3; i++)
            Near(state.WorldOf(state.Scene.Tracks[0]).Points[i].Position, state.Track.Points[i].Position);
    }

    [Fact]
    public void AnchorEditsAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        state.Cue();
        state.Play();

        Assert.NotNull(state.MoveAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
        Assert.NotNull(state.BringScene(Vector3.Zero));
    }

    [Fact]
    public void UndoingTheFirstPointDropsTheSceneAnchorSelection()
    {
        var state = new SessionState(() => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.SelectSceneAnchor();

        Assert.True(state.Undo());

        Assert.False(state.Scene.AnchorPlaced);
        Assert.Null(state.SelectedAnchor);
        Assert.NotNull(state.MoveAnchor(new Anchor(Vector3.Zero, 0f), carry: true));
    }

    [Fact]
    public void UndoingALaterTracksFirstPointDropsItsAnchorSelection()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(40f));
        state.SelectTrackAnchor(state.EditedTrackId);

        Assert.True(state.Undo());

        Assert.False(state.Scene.Tracks[1].AnchorPlaced);
        Assert.Null(state.SelectedAnchor);
    }

    [Fact]
    public void BringSceneIsRefusedUntilTheSceneAnchorIsPlaced()
    {
        var state = new SessionState(() => 1f);
        state.Edit();

        Assert.NotNull(state.BringScene(new Vector3(5f, 5f, 5f)));
        Assert.False(state.Scene.AnchorPlaced);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void MovingAnAnchorToWhereItIsRecordsNoStep()
    {
        var state = Editing();
        state.SelectSceneAnchor();

        Assert.Null(state.MoveAnchor(state.SelectedAnchorInWorld!.Value, carry: true));

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void DeletingAnotherTrackLeavesTheEditedTrackWhereItIs()
    {
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));
        var second = state.EditedTrackId;
        state.SwitchTrack(first);
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(-7f, 2f, 11f), 0.8f), carry: true);
        var before = state.Track.Points.Select(p => p.Position).ToList();

        Assert.Null(state.DeleteTrack(second));

        Assert.True(state.Scene.AnchorPlaced);
        for (var i = 0; i < before.Count; i++) Near(before[i], state.Track.Points[i].Position);
    }

    // Editing() with both anchors moved and turned by different, non-zero yaws.
    private static SessionState EditingWithTurnedAnchors()
    {
        var state = Editing();
        state.SelectSceneAnchor();
        state.MoveAnchor(new Anchor(new Vector3(100f, 1f, 20f), 0.7f), carry: true);
        state.SelectTrackAnchor(state.EditedTrackId);
        state.MoveAnchor(new Anchor(new Vector3(80f, 1f, 30f), -1.1f), carry: true);
        return state;
    }

    [Fact]
    public void AddingToTheEndLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        var target = new ControlPoint(new Vector3(123f, 4f, -56f), 1.234f, 0f, 1f);

        Assert.Null(state.AddToEnd(target));

        Near(target.Position, state.Track.Points[^1].Position);
        Assert.Equal(target.Yaw, state.Track.Points[^1].Yaw, Tolerance);
    }

    [Fact]
    public void AddingAfterSelectedLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        state.Select(1);
        var target = new ControlPoint(new Vector3(-30f, 2f, 77f), -0.4f, 0f, 1f);

        Assert.Null(state.AddAfterSelected(target));

        Near(target.Position, state.Track.Points[2].Position);
        Assert.Equal(target.Yaw, state.Track.Points[2].Yaw, Tolerance);
    }

    [Fact]
    public void ALivePreviewLandsWhereItWasPutUnderTurnedAnchors()
    {
        var state = EditingWithTurnedAnchors();
        var target = new ControlPoint(new Vector3(15f, 9f, -3f), 2.5f, 0f, 1f);

        state.BeginLiveEdit();
        Assert.Null(state.PreviewPoint(0, target));
        Near(target.Position, state.Track.Points[0].Position);
        Assert.Equal(target.Yaw, state.Track.Points[0].Yaw, Tolerance);
        state.EndLiveEdit();
    }
}
