using System.Numerics;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class SessionTimingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing, three points at x = 0, 10, 20 with keys at 0, 5 and 10 s, nothing selected.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
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
    public void SelectingAnInnerKeyLeavesThePointSelectionAlone()
    {
        var state = Editing();
        state.Select(2);
        Assert.Null(state.AddInnerKey(2f));
        Assert.Equal(1, state.SelectedKey);
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
    public void EasingIsOneUndoStep()
    {
        var state = Editing();
        Assert.Null(state.SetEasing(1, Easing.EaseOut));
        Assert.Equal(Easing.EaseOut, LegEasing.Read(state.Track, 1));
        Assert.True(state.Undo());
        Assert.Equal(Easing.Smooth, LegEasing.Read(state.Track, 1));
    }

    [Fact]
    public void AnAddedInnerKeySitsOnTheCurve()
    {
        var state = Editing();
        var before = state.Evaluator.DistanceAt(2.0);
        Assert.Null(state.AddInnerKey(2f));
        Assert.Equal(before, state.Evaluator.DistanceAt(2.0), 1);
    }

    [Fact]
    public void DeletingAKeyClearsTheTimingSelection()
    {
        var state = Editing();
        state.AddInnerKey(2f);
        Assert.Null(state.DeleteKey(1));
        Assert.Null(state.SelectedKey);
        Assert.Equal(3, state.Track.Timing.Count);
    }

    [Fact]
    public void AKeyDragIsOneStepComputedFromTheStart()
    {
        var state = Editing();
        state.BeginLiveEdit();
        Assert.Null(state.PreviewKeyMove(1, 3f, 0f));
        Assert.Null(state.PreviewKeyMove(1, 4f, 0f));
        state.EndLiveEdit();

        Assert.Equal(4f, state.Track.Timing[1].Time);
        Assert.True(state.Undo());
        Assert.Equal(5f, state.Track.Timing[1].Time);
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
    public void PointEditsClearAnInnerKeySelection()
    {
        var state = Editing();
        state.AddInnerKey(2f);
        state.AddToEnd(new ControlPoint(new Vector3(30f, 0f, 0f), 0f, 0f, 1f));
        Assert.Null(state.SelectedKey);
    }

    [Fact]
    public void TimingChangesOnlyWhileEditing()
    {
        var state = Editing();
        state.Play();
        Assert.NotNull(state.SetEasing(1, Easing.Linear));
        Assert.NotNull(state.AddInnerKey(2f));
    }
}
