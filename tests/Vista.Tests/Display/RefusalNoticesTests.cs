using Vista.Core.Display;
using Xunit;

namespace Vista.Tests.Display;

public class RefusalNoticesTests
{
    private const string Blank = "Enter a name.";
    private const string TooLong = "That name is too long.";

    [Fact]
    public void AMessageIsShownTheFirstTime()
    {
        Assert.True(new RefusalNotices().Show(Blank, 10.0));
    }

    [Fact]
    public void TheSameMessageWithinThreeSecondsOfShowingItIsNotShown()
    {
        var notices = new RefusalNotices();
        notices.Show(Blank, 10.0);

        // Shown at 10 s: 10 s and 12.9 s are both less than 3 s later.
        Assert.False(notices.Show(Blank, 10.0));
        Assert.False(notices.Show(Blank, 12.9));
    }

    [Fact]
    public void TheSameMessageThreeSecondsAfterShowingItIsShownAgain()
    {
        var notices = new RefusalNotices();
        notices.Show(Blank, 10.0);

        // 13 s is exactly 3 s after it was shown, no longer within 3 s.
        Assert.True(notices.Show(Blank, 13.0));
    }

    [Fact]
    public void AHiddenRepeatDoesNotRestartTheWait()
    {
        var notices = new RefusalNotices();
        notices.Show(Blank, 10.0);
        notices.Show(Blank, 12.0);

        // The wait counts from 10 s, when it was shown, not from the hidden repeat at 12 s, so 13 s shows it.
        Assert.True(notices.Show(Blank, 13.0));
    }

    [Fact]
    public void ADifferentMessageIsShownAtOnce()
    {
        var notices = new RefusalNotices();
        notices.Show(Blank, 10.0);

        Assert.True(notices.Show(TooLong, 10.5));
        // Each message waits on its own: Blank was shown at 10 s, so 11 s still hides it.
        Assert.False(notices.Show(Blank, 11.0));
    }
}
