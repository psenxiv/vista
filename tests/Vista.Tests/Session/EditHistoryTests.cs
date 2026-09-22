using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class EditHistoryTests
{
    private static EditSnapshot Snap(int? selected) => new(TrackEditing.Empty(), selected);

    [Fact]
    public void UndoReturnsTheRecordedStateAndRedoReturnsTheCurrentOne()
    {
        var history = new EditHistory();
        history.Record(Snap(1));

        Assert.Equal(1, history.Undo(Snap(2))!.Value.Selected);
        Assert.Equal(2, history.Redo(Snap(1))!.Value.Selected);
    }

    [Fact]
    public void RecordingClearsRedo()
    {
        var history = new EditHistory();
        history.Record(Snap(1));
        history.Undo(Snap(2));
        history.Record(Snap(3));

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(Snap(4)));
    }

    [Fact]
    public void OnlyTheLastHundredStepsAreKept()
    {
        var history = new EditHistory();
        for (var i = 0; i < EditHistory.Capacity + 5; i++) history.Record(Snap(i));

        var undone = 0;
        while (history.Undo(Snap(null)) is not null) undone++;

        Assert.Equal(EditHistory.Capacity, undone);
    }

    [Fact]
    public void NothingToUndoOrRedoReturnsNull()
    {
        var history = new EditHistory();
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo(Snap(null)));
        Assert.Null(history.Redo(Snap(null)));
    }
}
