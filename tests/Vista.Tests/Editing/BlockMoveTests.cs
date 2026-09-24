using CsCheck;
using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class BlockMoveTests
{
    [Fact]
    public void ASingleRowDroppedBelowGoesAfterTheTarget()
        // 0 1 2 3 4, 1 onto 3: 0 2 3 1 4.
        =>
        Assert.Equal([0, 2, 3, 1, 4], BlockMove.Order(5, [1], 1, 3)!);

    [Fact]
    public void ASingleRowDroppedAboveGoesBeforeTheTarget()
        // 0 1 2 3 4, 3 onto 1: 0 3 1 2 4.
        =>
        Assert.Equal([0, 3, 1, 2, 4], BlockMove.Order(5, [3], 3, 1)!);

    [Fact]
    public void ABlockKeepsItsOrderAndClosesItsGaps()
        // 0 1 2 3 4 5, 0 and 2 grabbed by 2 onto 4: rest 1 3 4 5, after 4: 1 3 4 0 2 5.
        =>
        Assert.Equal([1, 3, 4, 0, 2, 5], BlockMove.Order(6, [2, 0], 2, 4)!);

    [Fact]
    public void WhichSideDependsOnTheGrabbedRow()
    {
        // 1 and 4 onto 3: grabbed by 1, 3 is below, so after it (0 2 3 1 4 5); grabbed by 4, above, so before it (0 2 1 4 3 5).
        Assert.Equal([0, 2, 3, 1, 4, 5], BlockMove.Order(6, [1, 4], 1, 3)!);
        Assert.Equal([0, 2, 1, 4, 3, 5], BlockMove.Order(6, [1, 4], 4, 3)!);
    }

    [Fact]
    public void NoTargetMovesTheBlockToTheEnd() => Assert.Equal([0, 2, 1, 3], BlockMove.Order(4, [1, 3], 1, null)!);

    [Fact]
    public void DroppingOnAMovingRowMovesNothing() => Assert.Null(BlockMove.Order(4, [1, 2], 1, 2));

    [Fact]
    public void AnOrderThatChangesNothingIsNull() => Assert.Null(BlockMove.Order(4, [3], 3, null));

    [Fact]
    public void BadRowsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [], 0, 1));
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [3], 3, 1));
        // Row 1 of rows 1 and 3 exists; row 3 doesn't.
        Assert.Throws<ArgumentException>(() => BlockMove.Order(3, [1, 3], 1, 0));
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

    [Fact]
    [Trait("Category", "Property")]
    public void MovingRowsKeepsEveryRowAndMovesTheBlockBesideTheTarget()
    {
        var moves =
            from count in Gen.Int[1, 12]
            from moving in Gen.Int[0, count - 1].HashSet[1, count]
            from grabbed in Gen.OneOfConst(moving.ToArray())
            from target in Gen.Int[0, count - 1].Nullable()
            select (count, moving, grabbed, target);

        moves.Sample(
            move =>
            {
                var (count, moving, grabbed, target) = move;
                var order = BlockMove.Order(count, moving, grabbed, target);
                if (target is { } inside && moving.Contains(inside))
                {
                    Assert.Null(order);
                    return;
                }

                // Null when every row stays where it was.
                order ??= Enumerable.Range(0, count).ToArray();
                // Every row once.
                Assert.Equal(Enumerable.Range(0, count), order.Order());
                // The block is together and in its old order, and the other rows keep theirs.
                var block = moving.Order().ToArray();
                var start = Array.IndexOf(order, block[0]);
                Assert.Equal(block, order.Skip(start).Take(block.Length));
                Assert.Equal(
                    Enumerable.Range(0, count).Where(i => !moving.Contains(i)),
                    order.Where(i => !moving.Contains(i))
                );
                // Dropped at the end, at the end; on a row below the grabbed one, just after it; above, just before it.
                var end = start + block.Length;
                if (target is not { } t)
                    Assert.Equal(count, end);
                else if (t > grabbed)
                    Assert.Equal(t, order[start - 1]);
                else
                    Assert.Equal(t, order[end]);
            },
            iter: 100_000,
            print: Fixtures.Kept<(int Count, HashSet<int> Moving, int Grabbed, int? Target)>(move =>
                $"count {move.Count}, moving {string.Join(' ', move.Moving.Order())}, grabbed {move.Grabbed}, target {move.Target}"
            )
        );
    }
}
