using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionTimingTests
{
    // Editing, three points at x = 0, 10, 20 with keys at 0, 5 and 10 s, nothing selected.
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
    public void SelectingAPointSelectsItsKeyAndAPointKeySelectsItsPoint()
    {
        var state = Editing();
        state.Select(1);
        Assert.Equal(1, state.SelectedKey);

        state.SelectKey(2);
        Assert.Equal(2, state.Selected);

        state.SelectLeg(1);
        Assert.Equal((null, 1), (state.SelectedKey, state.SelectedLeg));
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void SelectingAHoldEndLeavesThePointSelectionAlone()
    {
        var state = Editing();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f)));
        state.Select(2);
        state.SelectKey(2);
        Assert.Equal(2, state.SelectedKey);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void DeselectingAPointClearsItsKeyButNotALeg()
    {
        var state = Editing();
        state.Select(1);
        state.Select(null);
        Assert.Null(state.SelectedKey);

        state.SelectLeg(2);
        state.Select(null);
        Assert.Equal(2, state.SelectedLeg);
    }

    [Fact]
    public void DeletingAPointClearsTheSelectedLeg()
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.SelectLeg(2);

        Assert.Null(state.DeletePoint(1));
        Assert.Null(state.SelectedLeg);
    }

    [Fact]
    public void LegAndHoldFieldEditsKeepTheSelectedLeg()
    {
        var state = Editing();
        state.SelectLeg(2);

        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 2, 8f)));
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f)));
        Assert.Equal(2, state.SelectedLeg);
    }

    [Fact]
    public void EasingIsOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetEasing(1, Easing.EaseOut));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(state.Track, 1));
        Assert.True(state.Undo());
        Assert.Equal(Easing.Smooth, LegEasing.Read(state.Track, 1));
    }

    [Fact]
    public void RemovingAHoldClearsTheTimingSelection()
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f));
        state.SelectKey(2);
        Assert.Null(state.RemoveHold(2));
        Assert.Null(state.SelectedKey);
        Assert.Equal(3, TrackEditing.KeyCount(state.Track));
    }

    [Fact]
    public void RemovingAHoldFromAPointKeyIsRefused()
    {
        var state = Editing();
        var before = state.Track;
        Assert.NotNull(state.RemoveHold(1));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void AKeyDragIsOneStepComputedFromTheStart()
    {
        var state = Editing();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewKeyMove(1, 3f));
        Assert.Null(state.PreviewKeyMove(1, 4f));
        state.EndLiveEdit();

        Assert.Equal(4f, state.Evaluator.Keys[1].Time, 3);
        Assert.Equal(10f, state.Duration, 3);
        Assert.True(state.Undo());
        Assert.Equal(5f, state.Evaluator.Keys[1].Time, 3);
        Assert.False(TrackEditing.IsPinned(state.Track, 1));
    }

    [Fact]
    public void AKeyDragBackToTheStartIsNoStep()
    {
        var state = Editing();
        var before = state.Track;
        var start = state.Evaluator.Keys[1].Time;
        state.BeginLiveEdit();
        Assert.Null(state.PreviewKeyMove(1, 3f));
        Assert.Null(state.PreviewKeyMove(1, start));
        state.EndLiveEdit();

        Assert.Same(before, state.Track);
        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void AnUnbrokenHandleDragKeepsBothSidesCollinearInTheGraph()
    {
        var state = Editing();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewHandle(1, KeySide.Out, 3f));
        state.EndLiveEdit();

        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.In), 1);
        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.Out), 1);
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 1));
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 2));
    }

    [Fact]
    public void AHandleDragKeepsItsShapeWhenTheTrackSpeedChanges()
    {
        var state = Editing();
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Null(state.SetTrackSpeed(4f));
        Assert.Equal(6f, state.Evaluator.SideSlope(1, KeySide.Out), 1);
    }

    [Fact]
    public void ABrokenHandleDragMovesOneSide()
    {
        var state = Editing();
        Assert.Null(state.BreakHandles(1));
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Equal(TangentMode.Auto, state.Track.Timing[1].InMode);
        Assert.Equal(TangentMode.Manual, state.Track.Timing[1].OutMode);
    }

    [Fact]
    public void UnifyGivesBothSidesTheChosenSidesSlope()
    {
        var state = Editing();
        state.BreakHandles(1);
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Null(state.UnifyHandles(1, KeySide.Out));
        Assert.False(state.Track.Timing[1].Broken);
        Assert.Equal(3f, state.Evaluator.SideSlope(1, KeySide.In), 1);
    }

    [Fact]
    public void PointEditsClearAHoldEndSelection()
    {
        var state = Editing();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f));
        state.SelectKey(2);
        state.AddToEnd(new ControlPoint(new Vector3(30f, 0f, 0f), 0f, 0f, 1f));
        Assert.Null(state.SelectedKey);
    }

    [Fact]
    public void SetTrackSpeedChangesDurationAndOneUndoRestoresIt()
    {
        var state = Editing();
        Assert.Null(state.SetTrackSpeed(5f));
        Assert.Equal(4.0, state.Duration, 3);
        Assert.True(state.Undo());
        Assert.Equal(10.0, state.Duration, 3);
    }

    [Fact]
    public void SetTrackDurationSetsTheTrackSpeed()
    {
        var state = Editing();
        Assert.Null(state.SetTrackDuration(20f));
        Assert.Equal(1f, state.Track.Speed, 3);
    }

    [Fact]
    public void SetLegDurationPinsTheLegAndResetLegUnpinsIt()
    {
        var state = Editing();
        Assert.Null(state.SetLegDuration(1, 2f));
        Assert.True(TrackEditing.IsPinned(state.Track, 1));
        Assert.Equal(2f, state.Evaluator.LegSeconds(1), 3);

        Assert.Null(state.ResetLeg(1));
        Assert.False(TrackEditing.IsPinned(state.Track, 1));
        Assert.Equal(5f, state.Evaluator.LegSeconds(1), 3);
    }

    [Fact]
    public void SetLegSpeedSetsTheLegsSeconds()
    {
        var state = Editing();
        Assert.Null(state.SetLegSpeed(2, 10f));
        Assert.Equal(1f, state.Evaluator.LegSeconds(2), 3);
    }

    [Fact]
    public void SetLegDurationKeepsTheSelectedLeg()
    {
        var state = Editing();
        state.SelectLeg(2);
        Assert.Null(state.SetLegDuration(2, 3f));
        Assert.Equal(2, state.SelectedLeg);
    }
}
