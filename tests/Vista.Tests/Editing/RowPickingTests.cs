using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class RowPickingTests
{
    private static readonly int[] Rows = [10, 11, 12, 13, 14];

    [Fact]
    public void APlainClickSelectsOnlyTheRowAndRangesFromIt()
    {
        var (selected, last) = RowPicking.Click(Rows, [10, 13], 13, 12, RowClick.Plain);
        Assert.Equal([12], selected);
        Assert.Equal(12, last);
    }

    [Fact]
    public void AToggleAddsAnUnselectedRowInListOrder()
    {
        var (selected, last) = RowPicking.Click(Rows, [13], 13, 11, RowClick.Toggle);
        Assert.Equal([11, 13], selected);
        Assert.Equal(11, last);
    }

    [Fact]
    public void AToggleRemovesASelectedRow()
    {
        var (selected, last) = RowPicking.Click(Rows, [11, 13], 11, 13, RowClick.Toggle);
        Assert.Equal([11], selected);
        Assert.Equal(13, last);
    }

    [Fact]
    public void ARangeDownAddsEveryRowFromTheLastClickedAndKeepsIt()
    {
        // Last clicked 11, Shift-click 13: 11, 12 and 13 join 14, which stays.
        var (selected, last) = RowPicking.Click(Rows, [11, 14], 11, 13, RowClick.Range);
        Assert.Equal([11, 12, 13, 14], selected);
        Assert.Equal(11, last);
    }

    [Fact]
    public void ARangeUpAddsTheRowsAboveTheLastClicked()
    {
        var (selected, _) = RowPicking.Click(Rows, [13], 13, 11, RowClick.Range);
        Assert.Equal([11, 12, 13], selected);
    }

    [Fact]
    public void ARangeWithNoLastClickedActsAsAToggle()
    {
        var (selected, last) = RowPicking.Click(Rows, [10], null, 12, RowClick.Range);
        Assert.Equal([10, 12], selected);
        Assert.Equal(12, last);
    }

    [Fact]
    public void ARangeFromARowThatIsGoneActsAsAToggle()
    {
        var (selected, last) = RowPicking.Click(Rows, [], 99, 12, RowClick.Range);
        Assert.Equal([12], selected);
        Assert.Equal(12, last);
    }

    [Fact]
    public void SelectedRowsThatAreGoneAreDropped()
    {
        var (selected, _) = RowPicking.Click(Rows, [99, 10], 10, 11, RowClick.Toggle);
        Assert.Equal([10, 11], selected);
    }

    [Fact]
    public void ClickingARowNotInTheListIsRefused() =>
        Assert.Throws<ArgumentException>(() => RowPicking.Click(Rows, [], null, 99, RowClick.Plain));

    [Theory]
    [InlineData(false, false, RowClick.Plain)]
    [InlineData(false, true, RowClick.Toggle)]
    [InlineData(true, false, RowClick.Range)]
    [InlineData(true, true, RowClick.Range)]
    public void ShiftWinsOverCtrl(bool shift, bool ctrl, RowClick expected) =>
        Assert.Equal(expected, RowPicking.FromKeys(shift, ctrl));
}
