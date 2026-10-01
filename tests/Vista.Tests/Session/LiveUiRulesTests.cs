using Vista.Core.Session;
using Xunit;

namespace Vista.Tests.Session;

public class LiveUiRulesTests
{
    [Theory]
    [InlineData(LiveUi.ShowAll, "Show all UI", "When Live: Show all UI")]
    [InlineData(LiveUi.HideGame, "Hide game UI", "When Live: Hide game UI")]
    [InlineData(LiveUi.HideAll, "Hide all UI", "When Live: Hide all UI")]
    public void EachChoiceHasItsLabelAndTheTooltipPutsTheHeadingBeforeIt(LiveUi ui, string label, string tooltip)
    {
        Assert.Equal(label, LiveUiRules.Label(ui));
        Assert.Equal(tooltip, LiveUiRules.Tooltip(ui));
    }

    [Fact]
    public void AnUnknownChoiceHasNoLabel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveUiRules.Label((LiveUi)99));
    }

    [Theory]
    [InlineData(LiveUi.ShowAll, false)]
    [InlineData(LiveUi.HideGame, true)]
    [InlineData(LiveUi.HideAll, true)]
    public void OnlyShowAllLeavesTheGameUiUp(LiveUi ui, bool hides)
    {
        Assert.Equal(hides, LiveUiRules.HidesGameUi(ui));
    }

    // Vista's windows stay only for Hide game UI, and only while Vista hid the UI: the game's own hide key hides them as before.
    [Theory]
    [InlineData(LiveUi.ShowAll, true, false)]
    [InlineData(LiveUi.HideGame, true, true)]
    [InlineData(LiveUi.HideGame, false, false)]
    [InlineData(LiveUi.HideAll, true, false)]
    public void VistasWindowsStayOnlyForHideGameUiWhileVistaHidIt(LiveUi ui, bool hiddenByVista, bool keeps)
    {
        Assert.Equal(keeps, LiveUiRules.KeepsWindows(ui, hiddenByVista));
    }

    // Vista stops drawing its windows only for Hide all UI, and only while Vista hid the UI.
    [Theory]
    [InlineData(LiveUi.ShowAll, true, false)]
    [InlineData(LiveUi.HideGame, true, false)]
    [InlineData(LiveUi.HideAll, true, true)]
    [InlineData(LiveUi.HideAll, false, false)]
    public void VistasWindowsHideOnlyForHideAllUiWhileVistaHidIt(LiveUi ui, bool hiddenByVista, bool hides)
    {
        Assert.Equal(hides, LiveUiRules.HidesWindows(ui, hiddenByVista));
    }

    [Theory]
    [InlineData(false, LiveUi.ShowAll)]
    [InlineData(true, LiveUi.HideAll)]
    public void TheOldSettingCarriesOverAsShowAllOrHideAll(bool hide, LiveUi ui)
    {
        Assert.Equal(ui, LiveUiRules.FromLegacy(hide));
    }
}
