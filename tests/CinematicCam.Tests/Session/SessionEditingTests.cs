using System.Numerics;
using CinematicCam.Core.Session;
using CinematicCam.Core.Tracks;
using Xunit;

namespace CinematicCam.Tests.Session;

public class SessionEditingTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing, three points at x = 0, 10, 20.
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
    public void AddToEndKeepsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.AddToEnd(Point(30f)));
        Assert.Equal(1, state.Selected);

        state.Select(null);
        state.AddToEnd(Point(40f));
        Assert.Null(state.Selected);
    }

    [Fact]
    public void AddAfterSelectedSelectsTheNewPoint()
    {
        var state = Editing();
        state.Select(0);
        var point = Point(5f);

        Assert.Null(state.AddAfterSelected(point));
        Assert.Equal(1, state.Selected);
        Assert.Same(point, state.Track.Points[1]);

        state.AddAfterSelected(Point(7f));
        Assert.Equal(2, state.Selected);
        Assert.Equal(7f, state.Track.Points[2].Position.X);
    }

    [Fact]
    public void SelectedOnlyEditsNeedASelection()
    {
        var state = Editing();
        const string refused = "Select a point first.";
        Assert.Equal(refused, state.AddAfterSelected(Point(5f)));
        Assert.Equal(refused, state.OverwriteSelected(Point(5f)));
        Assert.Equal(refused, state.DeleteSelected());
    }

    [Fact]
    public void OverwriteSelectedKeepsSelectionAndTiming()
    {
        var state = Editing();
        state.Select(1);
        var timing = state.Track.Timing;

        Assert.Null(state.OverwriteSelected(Point(12f)));
        Assert.Equal(1, state.Selected);
        Assert.Equal(12f, state.Track.Points[1].Position.X);
        Assert.Same(timing, state.Track.Timing);
    }

    [Fact]
    public void ReplacePointKeepsTheSelection()
    {
        var state = Editing();
        state.Select(2);
        Assert.Null(state.ReplacePoint(0, Point(-1f)));
        Assert.Equal(2, state.Selected);
        Assert.Equal(-1f, state.Track.Points[0].Position.X);
    }

    [Fact]
    public void DeleteSelectedClearsTheSelection()
    {
        var state = Editing();
        state.Select(1);
        Assert.Null(state.DeleteSelected());
        Assert.Null(state.Selected);
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Theory]
    [InlineData(1, 1, 3, 3)] // the selected point itself moves
    [InlineData(1, 0, 2, 0)] // a point before it moves past it
    [InlineData(1, 3, 0, 2)] // a point after it moves before it
    [InlineData(1, 2, 3, 1)] // a move entirely after it
    public void MoveKeepsTheSelectionOnTheSamePoint(int selected, int from, int to, int expected)
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Select(selected);
        var point = state.Track.Points[selected];

        Assert.Null(state.MovePoint(from, to));
        Assert.Equal(expected, state.Selected);
        Assert.Same(point, state.Track.Points[expected]);
    }

    [Fact]
    public void ChangeTrackClearsASelectionThatNoLongerExists()
    {
        var state = Editing();
        state.Select(2);
        state.ChangeTrack(_ => TrackEditing.Empty());
        Assert.Null(state.Selected);
    }

    [Fact]
    public void SelectOutOfRangeClears()
    {
        var state = Editing();
        state.Select(1);
        state.Select(5);
        Assert.Null(state.Selected);
    }

    [Fact]
    public void UndoAndRedoRestoreTrackAndSelection()
    {
        var state = Editing();
        state.Select(0);
        var before = state.Track;
        state.AddAfterSelected(Point(5f));
        var after = state.Track;

        Assert.True(state.Undo());
        Assert.Same(before, state.Track);
        Assert.Equal(0, state.Selected);

        Assert.True(state.Redo());
        Assert.Same(after, state.Track);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void UndoRestoresTheSelectionAfterAnOverwrite()
    {
        var state = Editing();
        state.Select(1);
        state.OverwriteSelected(Point(12f));

        state.Undo();
        Assert.Equal(1, state.Selected);
        Assert.Equal(10f, state.Track.Points[1].Position.X);
    }

    [Fact]
    public void ANewChangeClearsRedo()
    {
        var state = Editing();
        state.AddToEnd(Point(30f));
        state.Undo();
        state.AddToEnd(Point(40f));

        Assert.False(state.CanRedo);
        Assert.False(state.Redo());
    }

    [Fact]
    public void RefusedChangesRecordNothing()
    {
        var state = new SessionState();
        state.Edit();
        Assert.NotNull(state.DeleteSelected());
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void AChangeThatChangesNothingRecordsNothing()
    {
        var state = Editing();
        state.MovePoint(1, 1);

        Assert.True(state.Undo());
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void UndoRedoAndSelectWorkOnlyWhileEditing()
    {
        var state = Editing();
        state.Select(1);
        state.Play();

        Assert.False(state.CanUndo);
        Assert.False(state.Undo());
        state.Select(0);
        Assert.Equal(1, state.Selected);
        Assert.Equal("The track can only change while editing.", state.AddToEnd(Point(30f)));

        state.Edit();
        Assert.True(state.Undo());
    }
}
