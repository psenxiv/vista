using System.Numerics;
using Vista.Core.Editing;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionTimingTests
{
    [Fact]
    public void SelectingAPointSelectsItsKeyAndAPointKeySelectsItsPoint()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        Assert.Equal(1, state.Selection.Key);

        state.Selection.SelectKey(2);
        Assert.Equal(2, state.Selection.Point);

        state.Selection.SelectLeg(1);
        Assert.Equal((null, 1), (state.Selection.Key, state.Selection.Leg));
        Assert.Equal(2, state.Selection.Point);
    }

    [Fact]
    public void SelectingAHoldEndLeavesThePointSelectionAlone()
    {
        var state = EditingThreePoints();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f)));
        state.Selection.Select(2);
        state.Selection.SelectKey(2);
        Assert.Equal(2, state.Selection.Key);
        Assert.Equal(2, state.Selection.Point);
    }

    [Fact]
    public void DeselectingAPointClearsItsKeyButNotALeg()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.Selection.Select(null);
        Assert.Null(state.Selection.Key);

        state.Selection.SelectLeg(2);
        state.Selection.Select(null);
        Assert.Equal(2, state.Selection.Leg);
    }

    [Fact]
    public void DeselectingKeepsASelectedHoldEnd()
    {
        var state = EditingThreePoints();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f)));

        // The hold makes keys 0 and 1 for points 0 and 1, 2 for point 1's hold end, and 3 for point 2.
        state.Selection.SelectKey(2);
        state.Selection.Select(null);

        Assert.Equal(2, state.Selection.Key);
    }

    [Fact]
    public void AKeyOrLegPastTheEndSelectsNothing()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);

        // Three points: keys 0 to 2 and legs 1 and 2.
        state.Selection.SelectKey(3);
        Assert.Null(state.Selection.Key);
        Assert.Equal(1, state.Selection.Point);

        state.Selection.SelectLeg(3);
        Assert.Null(state.Selection.Leg);
    }

    [Fact]
    public void ATimingChangeKeepsTheSelectedKey()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);

        Assert.Null(state.SetTrackSpeed(4f));

        Assert.Equal(1, state.Selection.Key);
    }

    [Fact]
    public void UndoingToASelectionWithNoPointDropsThePointKey()
    {
        var state = EditingThreePoints();
        Assert.Null(state.AddToEnd(Point(30f)));
        state.Selection.Select(1);

        Assert.True(state.Undo());

        Assert.Null(state.Selection.Point);
        Assert.Null(state.Selection.Key);
    }

    [Fact]
    public void ALegSelectedBesideAPointStaysWhenThePointsDoNotChange()
    {
        var state = EditingThreePoints();
        state.Selection.Select(1);
        state.Selection.SelectLeg(2);

        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 2, 8f)));

        Assert.Equal((null, 2), (state.Selection.Key, state.Selection.Leg));
        Assert.Equal(1, state.Selection.Point);
    }

    [Fact]
    public void ALegStaysSelectedWhenAnEditLeavesThePointsEqual()
    {
        var state = EditingThreePoints();
        state.Selection.SelectLeg(2);

        Assert.Null(state.ChangeTrack(t => t with { Points = t.Points.ToList() }));

        Assert.Equal(2, state.Selection.Leg);
    }

    [Fact]
    public void APointEditClearsALegSelectedBesideSeveralPoints()
    {
        var state = EditingThreePoints();
        state.Selection.Select(0);
        state.Selection.ClickPoint(1, RowClick.Toggle);
        state.Selection.SelectLeg(2);

        Assert.Null(state.AddToEnd(Point(30f)));

        Assert.Null(state.Selection.Leg);
    }

    [Fact]
    public void DeletingAPointClearsTheSelectedLeg()
    {
        var state = EditingThreePoints();
        state.AddToEnd(Point(30f));
        state.Selection.SelectLeg(2);

        Assert.Null(state.DeletePoints([1]));
        Assert.Null(state.Selection.Leg);
    }

    [Fact]
    public void LegAndHoldFieldEditsKeepTheSelectedLeg()
    {
        var state = EditingThreePoints();
        state.Selection.SelectLeg(2);

        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 2, 8f)));
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f)));
        Assert.Equal(2, state.Selection.Leg);
    }

    [Fact]
    public void EasingIsOneUndoStep()
    {
        var state = EditingThreePoints();
        Assert.Null(state.SetEasing(1, Easing.EaseOut));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(state.Track, 1));
        Assert.True(state.Undo());
        Assert.Equal(Easing.Smooth, LegEasing.Read(state.Track, 1));
    }

    [Fact]
    public void RemovingAHoldClearsTheTimingSelection()
    {
        var state = EditingThreePoints();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f));
        state.Selection.SelectKey(2);
        Assert.Null(state.RemoveHold(2));
        Assert.Null(state.Selection.Key);
        Assert.Equal(3, TrackEditing.KeyCount(state.Track));
    }

    [Fact]
    public void RemovingAHoldFromAPointKeyIsRefused()
    {
        var state = EditingThreePoints();
        var before = state.Track;
        Assert.Equal("Only a hold end can remove its hold.", state.RemoveHold(1));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void AKeyDragIsOneStepComputedFromTheStart()
    {
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewKeyMove(1, 3f));
        Assert.Null(state.PreviewKeyMove(1, 4f));
        state.EndLiveEdit();

        Assert.Equal(4f, state.World.Evaluator.Keys[1].Time, 3);
        Assert.Equal(10f, state.Duration, 3);
        Assert.True(state.Undo());
        Assert.Equal(5f, state.World.Evaluator.Keys[1].Time, 3);
        Assert.False(TrackEditing.IsPinned(state.Track, 1));
    }

    [Fact]
    public void AKeyDragBackToTheStartIsNoStep()
    {
        var state = EditingThreePoints();
        var before = state.Track;
        var start = state.World.Evaluator.Keys[1].Time;
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
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewHandle(1, KeySide.Out, 3f));
        state.EndLiveEdit();

        Assert.Equal(3f, state.World.Evaluator.SideSlope(1, KeySide.In), 1);
        Assert.Equal(3f, state.World.Evaluator.SideSlope(1, KeySide.Out), 1);
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 1));
        Assert.Equal(Easing.Custom, LegEasing.Read(state.Track, 2));
    }

    [Fact]
    public void AHandleDragKeepsItsShapeWhenTheTrackSpeedChanges()
    {
        var state = EditingThreePoints();
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Null(state.SetTrackSpeed(4f));
        Assert.Equal(6f, state.World.Evaluator.SideSlope(1, KeySide.Out), 1);
    }

    [Fact]
    public void ABrokenHandleDragMovesOneSide()
    {
        var state = EditingThreePoints();
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
        var state = EditingThreePoints();
        state.BreakHandles(1);
        state.BeginLiveEdit();
        state.PreviewHandle(1, KeySide.Out, 3f);
        state.EndLiveEdit();

        Assert.Null(state.UnifyHandles(1, KeySide.Out));
        Assert.False(state.Track.Timing[1].Broken);
        Assert.Equal(3f, state.World.Evaluator.SideSlope(1, KeySide.In), 1);
    }

    [Fact]
    public void PointEditsClearAHoldEndSelection()
    {
        var state = EditingThreePoints();
        state.ChangeTrack(t => TrackEditing.SetHold(t, 1, 2f));
        state.Selection.SelectKey(2);
        state.AddToEnd(new ControlPoint(new Vector3(30f, 0f, 0f), 0f, 0f, 1f));
        Assert.Null(state.Selection.Key);
    }

    [Fact]
    public void SetTrackSpeedChangesDurationAndOneUndoRestoresIt()
    {
        var state = EditingThreePoints();
        Assert.Null(state.SetTrackSpeed(5f));
        Assert.Equal(4.0, state.Duration, 3);
        Assert.True(state.Undo());
        Assert.Equal(10.0, state.Duration, 3);
    }

    [Fact]
    public void SetTrackDurationSetsTheTrackSpeed()
    {
        var state = EditingThreePoints();
        Assert.Null(state.SetTrackDuration(20f));
        Assert.Equal(1f, state.Track.Speed, 3);
    }

    [Fact]
    public void SetLegDurationPinsTheLegAndResetLegUnpinsIt()
    {
        var state = EditingThreePoints();
        Assert.Null(state.SetLegDuration(1, 2f));
        Assert.True(TrackEditing.IsPinned(state.Track, 1));
        Assert.Equal(2f, state.World.Evaluator.LegSeconds(1), 3);

        Assert.Null(state.ResetLeg(1));
        Assert.False(TrackEditing.IsPinned(state.Track, 1));
        Assert.Equal(5f, state.World.Evaluator.LegSeconds(1), 3);
    }

    [Fact]
    public void SetLegSpeedSetsTheLegsSeconds()
    {
        var state = EditingThreePoints();
        Assert.Null(state.SetLegSpeed(2, 10f));
        Assert.Equal(1f, state.World.Evaluator.LegSeconds(2), 3);
    }

    [Fact]
    public void SetLegDurationKeepsTheSelectedLeg()
    {
        var state = EditingThreePoints();
        state.Selection.SelectLeg(2);
        Assert.Null(state.SetLegDuration(2, 3f));
        Assert.Equal(2, state.Selection.Leg);
    }
}
