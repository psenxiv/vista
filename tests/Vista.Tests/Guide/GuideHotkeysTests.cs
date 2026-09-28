using System.Text.RegularExpressions;
using Vista.Core.Input;
using Xunit;

namespace Vista.Tests.Guide;

/// <summary>Every Guide page's {key:...} tags and backticked keys stay in step with <see cref="HotkeyTable"/>.</summary>
public partial class GuideHotkeysTests
{
    // Kept bare for mouse gestures (Alt while dragging an anchor, Ctrl while dragging a timing
    // key, Ctrl/Shift + click); Shift alone collides with FlyFaster's own display name there,
    // but names no hotkey outside a gesture.
    private static readonly HashSet<string> BareModifiers = ["Ctrl", "Alt", "Shift"];

    private static string GuideFolder() => Path.Combine(Fixtures.RepositoryRoot(), "src", "Vista.Plugin", "Guide");

    private static IEnumerable<string> GuidePages() => Directory.EnumerateFiles(GuideFolder(), "*.md");

    /// <summary>Every hotkey's own display name, plus its bare key name and its alternate's, e.g. "Ctrl + Space", "Space", "Delete", "Backspace".</summary>
    private static HashSet<string> ForbiddenNames()
    {
        var names = new HashSet<string>();
        foreach (var entry in HotkeyTable.All)
        {
            names.Add(entry.Hotkey.DisplayName);
            names.Add(new Hotkey(entry.Hotkey.Key).DisplayName);
            if (entry.Hotkey.Alternate is { } alternate)
                names.Add(new Hotkey(alternate).DisplayName);
        }

        return names;
    }

    [Fact]
    public void EveryKeyTagInTheGuideNamesATableEntry()
    {
        foreach (var page in GuidePages())
        foreach (Match match in KeyTag().Matches(File.ReadAllText(page)))
        {
            var name = match.Groups["name"].Value;
            Assert.True(
                match.Value == $"{{key:{name}}}" && HotkeyTable.Find(name) is not null,
                $"{Path.GetFileName(page)} has {match.Value}, which is not a valid {{key:...}} tag naming a HotkeyTable entry."
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
        var forbidden = ForbiddenNames();

        foreach (var page in GuidePages())
        {
            var text = File.ReadAllText(page);
            foreach (Match match in Backticked().Matches(text))
            {
                var found = match.Groups["text"].Value;
                if (BareModifiers.Contains(found) && IsGesture(text, match.Index + match.Length))
                    continue;

                Assert.False(
                    forbidden.Contains(found),
                    $"{Path.GetFileName(page)} has `{found}` typed by hand instead of a {{key:...}} tag."
                );
            }
        }
    }

    /// <summary>True when the text at <paramref name="after"/> opens a mouse gesture: " + click" or " while".</summary>
    private static bool IsGesture(string text, int after) => Gesture().IsMatch(text[after..]);

    [GeneratedRegex(@"\{key:(?<name>[^}]*)\}", RegexOptions.IgnoreCase)]
    private static partial Regex KeyTag();

    [GeneratedRegex(@"`(?<text>[^`]+)`")]
    private static partial Regex Backticked();

    [GeneratedRegex(@"^( \+ click| while)")]
    private static partial Regex Gesture();
}
