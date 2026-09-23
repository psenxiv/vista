using Vista.Core.Guide;
using Xunit;

namespace Vista.Tests.Guide;

public class GuideMarkdownTests
{
    private static Run P(string text) => new(text, RunStyle.Plain);

    private static Run B(string text) => new(text, RunStyle.Bold);

    private static Run K(string text) => new(text, RunStyle.Key);

    [Fact]
    public void HeadingsKeepTheirLevel()
    {
        var blocks = GuideMarkdown.Parse("# One\n## Two\n### Three\n#### Four");

        // Blocks: One, the divider a section heading gets, Two, Three, then Four.
        var headings = blocks.OfType<Heading>().ToList();
        Assert.Equal([1, 2, 3], headings.Select(h => h.Level));
        Assert.Equal([P("Two")], headings[1].Runs);
        // Four marks is outside the subset, so it is a paragraph of its own text.
        Assert.Equal([P("#### Four")], Assert.IsType<Paragraph>(blocks[4]).Runs);
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
        Assert.Equal([K("E")], table.Rows[0][0]);
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
    public void BoldAndKeysSplitAParagraphIntoRuns()
    {
        var runs = Assert.IsType<Paragraph>(Assert.Single(GuideMarkdown.Parse("Press `Space` to **play**, then stop."))).Runs;

        Assert.Equal([P("Press "), K("Space"), P(" to "), B("play"), P(", then stop.")], runs);
    }

    [Fact]
    public void AnUnclosedMarkerStaysAsTyped()
    {
        Assert.Equal([P("a **b and `c")], GuideMarkdown.Inline("a **b and `c"));
    }

    [Fact]
    public void ALinkCarriesItsPage()
    {
        Assert.Equal([P("See "), new Run("Timing", RunStyle.Link, "timing.md"), P(" for more.")], GuideMarkdown.Inline("See [Timing](timing.md) for more."));
    }

    [Fact]
    public void AKeyCombinationSplitsIntoKeysJoinedByAPlainPlus()
    {
        Assert.Equal([P("Press "), K("Ctrl"), P(" + "), K("Space"), P(".")], GuideMarkdown.Inline("Press `Ctrl + Space`."));
    }

    [Fact]
    public void ASpanStartingWithASlashIsOneCommand()
    {
        // The " + " split applies to keys only, so a command keeps any plus it has.
        Assert.Equal([P("Type "), new Run("/vista a + b", RunStyle.Command)], GuideMarkdown.Inline("Type `/vista a + b`"));
    }

    [Fact]
    public void AnIconTagBecomesAnIconRunNamedByTheTag()
    {
        Assert.Equal([P("Click "), new Run("ChartLine", RunStyle.Icon), P(" to open it.")], GuideMarkdown.Inline("Click {icon:ChartLine} to open it."));
    }

    [Fact]
    public void AnIconTagWithNoNameStaysAsTyped()
    {
        Assert.Equal([P("a {icon:} b")], GuideMarkdown.Inline("a {icon:} b"));
    }

    [Fact]
    public void ASectionHeadingGetsADividerAboveIt()
    {
        var blocks = GuideMarkdown.Parse("# Page\nIntro.\n## Section\nText.");

        // Heading, paragraph, the added divider, then the section heading and its text.
        Assert.Equal(5, blocks.Count);
        Assert.IsType<Divider>(blocks[2]);
        Assert.Equal(2, Assert.IsType<Heading>(blocks[3]).Level);
    }

    [Fact]
    public void ASectionHeadingKeepsOneDividerWhenTheWriterAddedIt()
    {
        var blocks = GuideMarkdown.Parse("Intro.\n---\n## Section");

        Assert.Equal(3, blocks.Count);
        Assert.IsType<Divider>(blocks[1]);
        Assert.IsType<Heading>(blocks[2]);
    }

    [Fact]
    public void OnlySectionHeadingsAfterTheFirstBlockGetADivider()
    {
        // A page opening on a section heading has nothing above to divide; level 1 and 3 headings never get one.
        var blocks = GuideMarkdown.Parse("## First\n# Title\n### Sub");

        Assert.Equal([2, 1, 3], blocks.Select(b => Assert.IsType<Heading>(b).Level));
    }
}
