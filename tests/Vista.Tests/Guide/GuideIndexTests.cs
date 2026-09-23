using Vista.Core.Guide;
using Xunit;

namespace Vista.Tests.Guide;

public class GuideIndexTests
{
    [Fact]
    public void AFlatListKeepsItsOrder()
    {
        var topics = GuideIndex.Parse("- [Getting started](getting-started.md)\n- [Hotkeys](hotkeys.md)\n");

        Assert.Equal(["Getting started", "Hotkeys"], topics.Select(t => t.Title));
        Assert.Equal(["getting-started.md", "hotkeys.md"], topics.Select(t => t.File));
        Assert.All(topics, t => Assert.Empty(t.Children));
    }

    [Fact]
    public void AnIndentedEntryIsASubTopicOfTheOneAbove()
    {
        var topics = GuideIndex.Parse("""
            - [Aim](aim.md)
              - [Look At](aim-look-at.md)
              - [Watch Target](aim-watch.md)
            - [Playlist](playlist.md)
            """);

        Assert.Equal(["Aim", "Playlist"], topics.Select(t => t.Title));
        Assert.Equal(["Look At", "Watch Target"], topics[0].Children.Select(t => t.Title));
        Assert.Equal("aim-look-at.md", topics[0].Children[0].File);
        Assert.Empty(topics[1].Children);
    }

    [Fact]
    public void LinesThatAreNotLinkedBulletsAreSkipped()
    {
        var topics = GuideIndex.Parse("# Contents\n\nSome words.\n- [Timing](timing.md)\n- not a link\n");

        Assert.Equal(["Timing"], topics.Select(t => t.Title));
    }
}
