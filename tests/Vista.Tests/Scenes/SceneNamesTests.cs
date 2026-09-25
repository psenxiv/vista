using Vista.Core.Scenes;
using Xunit;

namespace Vista.Tests.Scenes;

public class SceneNamesTests
{
    private const string Unusable = "That name can't be used as a file name.";

    [Theory]
    [InlineData("Scene 1")]
    [InlineData("  Scene 1  ")]
    [InlineData("Scene.x")]
    [InlineData("COM10")]
    [InlineData("CONSOLE")]
    public void ValidNamesHaveNoRefusal(string name) => Assert.Null(SceneNames.Refusal(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNameAsksForOne(string name) => Assert.Equal("Enter a name.", SceneNames.Refusal(name));

    [Fact]
    public void LengthIsCountedAfterTrimming()
    {
        Assert.Null(SceneNames.Refusal(new string('a', 64)));
        Assert.Null(SceneNames.Refusal($"  {new string('a', 64)}  "));
        Assert.Equal("That name is too long.", SceneNames.Refusal(new string('a', 65)));
    }

    [Theory]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a:b")]
    [InlineData("a\"b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a|b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a\tb")]
    [InlineData("a\u0001b")]
    [InlineData("Scene.")]
    [InlineData("Scene. ")]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("Aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("COM9")]
    [InlineData("lpt1")]
    [InlineData("LPT9")]
    [InlineData(" CON ")]
    public void FileNameUnsafeNamesAreRefused(string name) => Assert.Equal(Unusable, SceneNames.Refusal(name));

    [Fact]
    public void TakenIgnoresCaseAndSurroundingSpace()
    {
        string[] existing = ["Scene 1", "Dolly"];

        Assert.True(SceneNames.Taken("scene 1", existing));
        Assert.True(SceneNames.Taken(" DOLLY ", existing));
        Assert.False(SceneNames.Taken("Scene 2", existing));
    }

    [Fact]
    public void NextFreeTakesTheFirstUnusedNumberFromOne()
    {
        Assert.Equal("Scene 1", SceneNames.NextFree("Scene", []));
        Assert.Equal("Scene 1", SceneNames.NextFree("Scene", ["Scene 2"]));
        Assert.Equal("Scene 3", SceneNames.NextFree("Scene", ["Scene 1", "scene 2"]));
    }

    [Fact]
    public void CopyOfAddsCopyThenNumbersFromTwo()
    {
        Assert.Equal("Dolly copy", SceneNames.CopyOf("Dolly", ["Dolly"]));
        Assert.Equal("Dolly copy 2", SceneNames.CopyOf("Dolly", ["Dolly", "Dolly copy"]));
        Assert.Equal("Dolly copy 3", SceneNames.CopyOf("Dolly", ["dolly COPY", "Dolly copy 2"]));
    }

    // A preset name is checked by the name rules alone, and replaces a taken one (trimmed, ignoring case) only when usable.

    [Fact]
    public void APresetNameReplacesATakenOneOnlyWhenUsable()
    {
        Assert.Equal((null, true), SceneNames.PresetCheck("Dusk ", ["dusk"]));
        Assert.Equal((null, false), SceneNames.PresetCheck("Dawn", ["Dusk"]));
        Assert.Equal(("Enter a name.", false), SceneNames.PresetCheck("  ", ["Dusk"]));
        Assert.Equal((Unusable, false), SceneNames.PresetCheck("a/b", ["a/b"]));
    }
}
