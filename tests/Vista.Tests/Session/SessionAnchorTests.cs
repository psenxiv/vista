using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionAnchorTests
{
    // These tests put every point at head height, so y defaults to 5 here.
    private static ControlPoint Point(float x, float y = 5f, float z = 0f) => Fixtures.Point(x, y, z);

    // Editing with the ground at y = 1; Track 1 has points at x = 10, 20, 30 (y = 5).
    private static SessionState Editing()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddToEnd(Point(30f));
        return state;
    }

    [Fact]
    public void TheFirstPointPlacesBothAnchorsOnTheGroundUnderIt()
    {
        var state = Editing();

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
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(50f, 7f, 5f));

        Assert.Equal(new Vector3(10f, 1f, 0f), state.Scene.Anchor.Position);
        Near(new Vector3(50f, 1f, 5f), SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[1]).Position, 1e-4f);
        Near(new Vector3(50f, 7f, 5f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void WorldOfKeepsTheSameInstanceUntilSomethingChanges()
    {
        var state = Editing();
        Assert.Same(state.Track, state.Track);
        Assert.Same(state.World.WorldOf(state.Scene.Tracks[0]), state.Track);
    }

    [Fact]
    public void SelectingAnAnchorClearsThePointAndSelectingAPointClearsTheAnchor()
    {
        var state = Editing();
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
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));

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
    public void MovingTheSceneAnchorCarriesThePointsAsOneUndoStep()
    {
        var state = Editing();
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
        var state = Editing();
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
        var state = Editing();
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
        var state = Editing();
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

        state.AddToEnd(Point(10f));

        Assert.Equal(new Vector3(10f, 5f, 0f), asked);
        Assert.Equal(2f, state.Scene.Anchor.Position.Y);
        Assert.Equal(2f, SceneGeometry.WorldAnchor(state.Scene, state.Scene.Tracks[0]).Position.Y);
    }

    [Fact]
    public void WithNoGroundTheAnchorsSitAtThePointsHeight()
    {
        var state = new SessionState(_ => null);
        state.Edit();

        state.AddToEnd(Point(10f));

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
        state.AddToEnd(Point(10f));
        Assert.Equal(1, reads);

        state.AddToEnd(Point(20f));
        state.Selection.Select(0);
        state.AddAfterSelected(Point(15f));

        Assert.Equal(1, reads);
        Assert.Equal(3, state.Track.Points.Count);
    }

    [Fact]
    public void ClearKeepsTheAnchorSoTheNextPointIsNotReplaced()
    {
        var state = Editing();
        state.Selection.SelectTrackAnchor(state.EditedTrackId);
        state.BeginLiveEdit();
        state.PreviewAnchor(new Anchor(new Vector3(0f, 0f, 0f), 0f), carry: false);
        state.EndLiveEdit();
        var anchor = state.Scene.Tracks[0].Anchor;

        state.ChangeTrack(TrackEditing.Clear);
        state.AddToEnd(Point(90f));

        Assert.Equal(anchor, state.Scene.Tracks[0].Anchor);
        Near(new Vector3(90f, 5f, 0f), state.Track.Points[0].Position, 1e-4f);
    }

    [Fact]
    public void ADuplicateSitsOnTheOriginal()
    {
        var state = Editing();
        state.DuplicateTrack(state.EditedTrackId);

        for (var i = 0; i < 3; i++)
            Near(state.World.WorldOf(state.Scene.Tracks[0]).Points[i].Position, state.Track.Points[i].Position, 1e-4f);
    }

    [Fact]
    public void UndoingTheFirstPointDropsTheSceneAnchorSelection()
    {
        var state = new SessionState(_ => 1f);
        state.Edit();
        state.AddToEnd(Point(10f));
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
        var state = Editing();
        state.AddTrack();
        state.AddToEnd(Point(40f));
        state.Selection.SelectTrackAnchor(state.EditedTrackId);

        Assert.True(state.Undo());

        Assert.False(state.Scene.Tracks[1].AnchorPlaced);
        Assert.Null(state.Selection.Anchor);
    }

    [Fact]
    public void MovingAnAnchorToWhereItIsRecordsNoStep()
    {
        var state = Editing();
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
        var state = Editing();
        var first = state.EditedTrackId;
        state.AddTrack();
        state.AddToEnd(Point(40f));
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

    // Editing() with both anchors moved and turned by different, non-zero yaws.
    private static SessionState EditingWithTurnedAnchors()
    {
        var state = Editing();
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
}
