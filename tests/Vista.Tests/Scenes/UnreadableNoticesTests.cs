using Vista.Core.Scenes;
using Xunit;

namespace Vista.Tests.Scenes;

public class UnreadableNoticesTests
{
    [Fact]
    public void AFileLeftOutOfTheListIsNamedTheFirstTime()
    {
        Assert.Equal(
            "Could not read Broken, so it isn't listed. The file may be damaged.",
            new UnreadableNotices().Unlisted("Broken")
        );
    }

    [Fact]
    public void AFileIsNamedOnlyOnceIgnoringCase()
    {
        var notices = new UnreadableNotices();
        notices.Unlisted("Broken");

        Assert.Null(notices.Unlisted("Broken"));
        Assert.Null(notices.Unlisted("BROKEN"));
    }

    [Fact]
    public void AnotherFileIsNamedInItsTurn()
    {
        var notices = new UnreadableNotices();
        notices.Unlisted("Broken");

        Assert.Equal("Could not read Torn, so it isn't listed. The file may be damaged.", notices.Unlisted("Torn"));
    }

    [Fact]
    public void AFileNamedAsReplacedIsNotNamedAgainAsUnlisted()
    {
        var notices = new UnreadableNotices();

        Assert.Equal(
            "Could not read Broken, so Dawn is open instead. The file may be damaged.",
            notices.Replaced("Broken", "Dawn")
        );
        Assert.Null(notices.Unlisted("Broken"));
    }
}
