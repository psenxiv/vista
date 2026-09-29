using Vista.Core.Scenes;
using Xunit;

namespace Vista.Tests.Scenes;

public class FormatVersionsTests
{
    [Fact]
    public void AMissingEntryCountsAsTheFirstFormatSoTheUpgradeIsDue()
    {
        // No entry means format 1; scenes are written as format 2 and 1 < 2.
        Assert.True(FormatVersions.SceneUpgradeDue(new Dictionary<string, int>()));
    }

    [Fact]
    public void AnEntryBelowTheCurrentFormatIsDue()
    {
        // 1 < 2.
        Assert.True(FormatVersions.SceneUpgradeDue(new Dictionary<string, int> { ["scenes"] = 1 }));
    }

    [Fact]
    public void AnEntryAtTheCurrentFormatIsNotDue()
    {
        // 2 is not below 2.
        Assert.False(FormatVersions.SceneUpgradeDue(new Dictionary<string, int> { ["scenes"] = 2 }));
    }

    [Fact]
    public void AnEntryFromANewerVistaIsNotDue()
    {
        // 3 is not below 2, so an older Vista never sweeps a folder a newer one has upgraded.
        Assert.False(FormatVersions.SceneUpgradeDue(new Dictionary<string, int> { ["scenes"] = 3 }));
    }

    [Fact]
    public void RecordingScenesOnAnEmptyMapLeavesTheCurrentFormat()
    {
        var versions = new Dictionary<string, int>();

        FormatVersions.RecordScenes(versions);

        Assert.Equal(new Dictionary<string, int> { ["scenes"] = 2 }, versions);
    }

    [Fact]
    public void RecordingScenesReplacesAnOlderEntryAndKeepsOtherKeys()
    {
        var versions = new Dictionary<string, int> { ["scenes"] = 1, ["presets"] = 7 };

        FormatVersions.RecordScenes(versions);

        Assert.Equal(new Dictionary<string, int> { ["scenes"] = 2, ["presets"] = 7 }, versions);
    }
}
