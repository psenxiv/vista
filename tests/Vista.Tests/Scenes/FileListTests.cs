using Vista.Core.Scenes;
using Xunit;

namespace Vista.Tests.Scenes;

public sealed class FileListTests
{
    private static readonly DateTime Stamp = new(2026, 1, 1);

    private static FileEntry Entry(string name) => new(name, Stamp, null);

    [Fact]
    public void EmptySearchListsEveryEntryOrderedByNameIgnoringCase()
    {
        var entries = new[] { Entry("gamma"), Entry("Alpha"), Entry("beta") };

        var filtered = FileList.Filter(entries, string.Empty, e => e.Name);

        Assert.Equal(["Alpha", "beta", "gamma"], filtered.Select(e => e.Name));
    }

    [Fact]
    public void SearchIsTrimmedAndMatchesAnywhereInTheNameIgnoringCase()
    {
        var entries = new[] { Entry("Dawn Patrol"), Entry("Dusk"), Entry("Midday") };

        var filtered = FileList.Filter(entries, "  aWn  ", e => e.Name);

        Assert.Equal(["Dawn Patrol"], filtered.Select(e => e.Name));
    }

    [Fact]
    public void NoMatchIsAnEmptyList()
    {
        var entries = new[] { Entry("Dawn"), Entry("Dusk") };

        Assert.Empty(FileList.Filter(entries, "midday", e => e.Name));
    }

    [Fact]
    public void MatchesAreOrderedByNameIgnoringCaseRegardlessOfInputOrder()
    {
        var entries = new[] { Entry("gamma take"), Entry("Alpha take"), Entry("beta take") };

        var filtered = FileList.Filter(entries, "take", e => e.Name);

        Assert.Equal(["Alpha take", "beta take", "gamma take"], filtered.Select(e => e.Name));
    }
}
