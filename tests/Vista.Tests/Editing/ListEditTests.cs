using Vista.Core.Editing;
using Xunit;

namespace Vista.Tests.Editing;

public class ListEditTests
{
    private static readonly int[] Items = [10, 11, 12];

    [Fact]
    public void IndexOfFindsTheFirstMatchOrMinusOne()
    {
        Assert.Equal(1, ListEdit.IndexOf(Items, i => i > 10));
        Assert.Equal(-1, ListEdit.IndexOf(Items, i => i > 20));
    }

    [Fact]
    public void ReplaceCopiesTheListWithOneItemChanged()
    {
        Assert.Equal([10, 99, 12], ListEdit.Replace(Items, 1, 99));
        Assert.Equal([10, 11, 12], Items);
    }

    [Fact]
    public void InsertPutsAnItemBeforeTheIndexOrAtTheEnd()
    {
        Assert.Equal([99, 10, 11, 12], ListEdit.Insert(Items, 0, 99));
        Assert.Equal([10, 11, 12, 99], ListEdit.Insert(Items, 3, 99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ListEdit.Insert(Items, 4, 99));
    }

    [Fact]
    public void InsertRangeKeepsTheInsertedOrder() =>
        Assert.Equal([10, 1, 2, 11, 12], ListEdit.InsertRange(Items, 1, [1, 2]));
}
