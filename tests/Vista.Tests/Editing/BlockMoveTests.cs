using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class BlockMoveTests
{
    [Fact]
    public void ASingleRowDroppedBelowGoesAfterTheTarget()
        // 0 1 2 3 4, 1 onto 3: 0 2 3 1 4.
        => Assert.Equal([0, 2, 3, 1, 4], BlockMove.Order(5, [1], 1, 3));

    [Fact]
    public void ASingleRowDroppedAboveGoesBeforeTheTarget()
        // 0 1 2 3 4, 3 onto 1: 0 3 1 2 4.
        => Assert.Equal([0, 3, 1, 2, 4], BlockMove.Order(5, [3], 3, 1));

    [Fact]
    public void ABlockKeepsItsOrderAndClosesItsGaps()
        // 0 1 2 3 4 5, 0 and 2 grabbed by 2 onto 4: rest 1 3 4 5, after 4: 1 3 4 0 2 5.
        => Assert.Equal([1, 3, 4, 0, 2, 5], BlockMove.Order(6, [2, 0], 2, 4));

    [Fact]
    public void WhichSideDependsOnTheGrabbedRow()
    {
        // 1 and 4 onto 3: grabbed by 1, 3 is below, so after it (0 2 3 1 4 5); grabbed by 4, above, so before it (0 2 1 4 3 5).
        Assert.Equal([0, 2, 3, 1, 4, 5], BlockMove.Order(6, [1, 4], 1, 3));
        Assert.Equal([0, 2, 1, 4, 3, 5], BlockMove.Order(6, [1, 4], 4, 3));
    }

    [Fact]
    public void NoTargetMovesTheBlockToTheEnd()
        => Assert.Equal([0, 2, 1, 3], BlockMove.Order(4, [1, 3], 1, null));

    [Fact]
    public void DroppingOnAMovingRowMovesNothing()
        => Assert.Null(BlockMove.Order(4, [1, 2], 1, 2));

    [Fact]
    public void AnOrderThatChangesNothingIsNull()
        => Assert.Null(BlockMove.Order(4, [3], 3, null));

    [Fact]
    public void BadRowsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [], 0, 1));
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [3], 3, 1));
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [1], 0, 2));
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [1], 1, 3));
    }

    [Fact]
    public void ApplyTakesItemsInTheOrderAndRefusesABadOne()
    {
        Assert.Equal(["c", "a", "b"], BlockMove.Apply(["a", "b", "c"], [2, 0, 1]));
        Assert.Throws<ArgumentException>(() => BlockMove.Apply(["a", "b"], [0, 0]));
        Assert.Throws<ArgumentException>(() => BlockMove.Apply(["a", "b"], [0]));
        Assert.Throws<ArgumentException>(() => BlockMove.Apply(["a", "b"], [0, 2]));
    }

    [Fact]
    public void NewIndexFindsWhereARowWent()
    {
        Assert.Equal(0, BlockMove.NewIndex([2, 0, 1], 2));
        Assert.Equal(2, BlockMove.NewIndex([2, 0, 1], 1));
    }
}
