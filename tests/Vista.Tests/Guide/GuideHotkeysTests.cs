using System.Text.RegularExpressions;
using Vista.Core.Input;
using Xunit;

namespace Vista.Tests.Guide;

/// <summary>Every Guide page's {key:...} tags and backticked keys stay in step with <see cref="HotkeyTable"/>.</summary>
public partial class GuideHotkeysTests
{
    // Kept bare for mouse gestures (Alt while dragging an anchor, Ctrl while dragging a timing
    // key, Ctrl/Shift + click); Shift alone collides with FlyFaster's own display name there,
    // but names no hotkey in that context.
    private static readonly HashSet<string> BareModifiers = ["Ctrl", "Alt", "Shift"];

    private static string GuideFolder() => Path.Combine(Fixtures.RepositoryRoot(), "src", "Vista.Plugin", "Guide");

    private static IEnumerable<string> GuidePages() => Directory.EnumerateFiles(GuideFolder(), "*.md");

    [Fact]
    public void EveryKeyTagInTheGuideNamesATableEntry()
    {
        foreach (var page in GuidePages())
        foreach (Match match in KeyTag().Matches(File.ReadAllText(page)))
        {
            var name = match.Groups["name"].Value;
            Assert.True(
                HotkeyTable.Find(name) is not null,
                $"{Path.GetFileName(page)} has {{key:{name}}}, which names no HotkeyTable entry."
            );
        }
    }

    [Fact]
    public void EveryHotkeyTableEntryAppearsInTheHotkeysPage()
    {
        var hotkeysPage = File.ReadAllText(Path.Combine(GuideFolder(), "hotkeys.md"));

        foreach (var entry in HotkeyTable.All)
            Assert.Contains($"{{key:{entry.Name}}}", hotkeysPage);
    }

    [Fact]
    public void NoBacktickedTextEqualsATableEntrysDisplayName()
    {
        var displayNames = HotkeyTable.All.Select(entry => entry.Hotkey.DisplayName).ToHashSet();

        foreach (var page in GuidePages())
        foreach (Match match in Backticked().Matches(File.ReadAllText(page)))
        {
            var text = match.Groups["text"].Value;
            if (BareModifiers.Contains(text))
                continue;

            Assert.False(
                displayNames.Contains(text),
                $"{Path.GetFileName(page)} has `{text}` typed by hand instead of a {{key:...}} tag."
            );
        }
    }

    [GeneratedRegex(@"\{key:(?<name>[A-Za-z0-9]+)\}")]
    private static partial Regex KeyTag();

    [GeneratedRegex(@"`(?<text>[^`]+)`")]
    private static partial Regex Backticked();
}
