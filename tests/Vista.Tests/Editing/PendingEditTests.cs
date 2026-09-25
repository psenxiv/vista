using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class PendingEditTests
{
    [Fact]
    public void AHeldValueShowsInItsFieldAlone()
    {
        var edit = new PendingEdit<float>(() => true);
        edit.Hold("speed", 3f, 2f, _ => { });

        Assert.Equal(3f, edit.ValueOr("speed", 2f));
        Assert.Equal(7f, edit.ValueOr("hold", 7f));
        Assert.Equal("speed", edit.HeldBy);
    }

    [Fact]
    public void CommitAppliesAChangedValueOnce()
    {
        var applied = new List<int>();
        var edit = new PendingEdit<int>(() => true);
        edit.Hold("loops", 4, 2, applied.Add);

        edit.Commit();
        edit.Commit();

        Assert.Equal([4], applied);
        Assert.Null(edit.HeldBy);
    }

    [Fact]
    public void AValueDraggedBackToWhereItStartedIsNotApplied()
    {
        var applied = new List<int>();
        var edit = new PendingEdit<int>(() => true);
        edit.Hold("loops", 3, 2, applied.Add);
        // Dragged on, then back to the 2 the field showed before the drag.
        edit.Hold("loops", 2, 2, applied.Add);

        edit.Commit();

        Assert.Empty(applied);
        Assert.Null(edit.HeldBy);
    }

    [Fact]
    public void AValueThatCanNoLongerApplyIsDropped()
    {
        var applied = new List<float>();
        var editing = true;
        var edit = new PendingEdit<float>(() => editing);
        edit.Hold("smoothing", 0.5f, 0.25f, applied.Add);

        editing = false;
        edit.Commit();

        Assert.Empty(applied);
        Assert.Null(edit.HeldBy);
    }

    [Fact]
    public void CommittingAnotherFieldLeavesTheHeldValue()
    {
        var applied = new List<float>();
        var edit = new PendingEdit<float>(() => true);
        edit.Hold("look-ahead", 1.5f, 1f, applied.Add);

        edit.Commit("track-speed");
        Assert.Empty(applied);

        edit.Commit("look-ahead");
        Assert.Equal([1.5f], applied);
    }

    [Fact]
    public void HoldingAnotherFieldReplacesTheHeldValue()
    {
        var applied = new List<string>();
        var edit = new PendingEdit<float>(() => true);
        edit.Hold("speed", 3f, 2f, _ => applied.Add("speed"));
        edit.Hold("hold", 1f, 0f, _ => applied.Add("hold"));

        edit.Commit();

        Assert.Equal(["hold"], applied);
    }

    [Fact]
    public void ClearDropsTheHeldValue()
    {
        var applied = new List<float>();
        var edit = new PendingEdit<float>(() => true);
        edit.Hold("speed", 3f, 2f, applied.Add);

        edit.Clear();
        edit.Commit();

        Assert.Empty(applied);
        Assert.Equal(2f, edit.ValueOr("speed", 2f));
    }
}
