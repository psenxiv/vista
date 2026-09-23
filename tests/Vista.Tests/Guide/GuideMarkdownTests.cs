using Vista.Core.Guide;
using Xunit;

namespace Vista.Tests.Guide;

public class GuideMarkdownTests
{
    private static Run P(string text) => new(text, RunStyle.Plain);

    private static Run B(string text) => new(text, RunStyle.Bold);

    private static Run C(string text) => new(text, RunStyle.Code);

    [Fact]
    public void HeadingsKeepTheirLevel()
    {
        var blocks = GuideMarkdown.Parse("# One\n## Two\n### Three\n#### Four");

        Assert.Equal([1, 2, 3], blocks.Take(3).Select(b => Assert.IsType<Heading>(b).Level));
        Assert.Equal([P("Two")], ((Heading)blocks[1]).Runs);
        // Four marks is outside the subset, so it is a paragraph of its own text.
        Assert.Equal([P("#### Four")], Assert.IsType<Paragraph>(blocks[3]).Runs);
    }

    [Fact]
    public void WrappedLinesJoinIntoOneParagraphAndBlankLinesSplitThem()
    {
        var blocks = GuideMarkdown.Parse("First line\nsecond line.\n\nNext paragraph.");

        Assert.Equal(2, blocks.Count);
        Assert.Equal([P("First line second line.")], Assert.IsType<Paragraph>(blocks[0]).Runs);
        Assert.Equal([P("Next paragraph.")], Assert.IsType<Paragraph>(blocks[1]).Runs);
    }

    [Fact]
    public void BulletAndNumberedListsCollectTheirItems()
    {
        var blocks = GuideMarkdown.Parse("- one\n* two\n  continued\n\n1. first\n2. second");

        var bullets = Assert.IsType<BulletList>(blocks[0]);
        Assert.Equal(2, bullets.Items.Count);
        Assert.Equal([P("two continued")], bullets.Items[1]);
        var numbered = Assert.IsType<NumberedList>(blocks[1]);
        Assert.Equal([[P("first")], [P("second")]], numbered.Items);
    }

    [Fact]
    public void ATableSplitsItsHeaderFromItsRowsAndTrimsCells()
    {
        var blocks = GuideMarkdown.Parse("| Key | Does |\n|---|---|\n|  `E`  | Fly **up** |\n| Q | Fly down |");

        var table = Assert.IsType<Table>(Assert.Single(blocks));
        Assert.Equal([[P("Key")], [P("Does")]], table.Header);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal([C("E")], table.Rows[0][0]);
        Assert.Equal([P("Fly "), B("up")], table.Rows[0][1]);
        Assert.Equal([P("Q")], table.Rows[1][0]);
    }

    [Fact]
    public void ThreeDashesAloneAreADivider()
    {
        var blocks = GuideMarkdown.Parse("Above.\n---\nBelow.");

        Assert.IsType<Paragraph>(blocks[0]);
        Assert.IsType<Divider>(blocks[1]);
        Assert.IsType<Paragraph>(blocks[2]);
    }

    [Fact]
    public void BoldAndCodeSplitAParagraphIntoRuns()
    {
        var runs = Assert.IsType<Paragraph>(Assert.Single(GuideMarkdown.Parse("Press `Space` to **play**, then stop."))).Runs;

        Assert.Equal([P("Press "), C("Space"), P(" to "), B("play"), P(", then stop.")], runs);
    }

    [Fact]
    public void AnUnclosedMarkerStaysAsTyped()
    {
        Assert.Equal([P("a **b and `c")], GuideMarkdown.Inline("a **b and `c"));
    }

    [Fact]
    public void ALinkShowsOnlyItsText()
    {
        Assert.Equal([P("See Timing for more.")], GuideMarkdown.Inline("See [Timing](timing.md) for more."));
    }
}
