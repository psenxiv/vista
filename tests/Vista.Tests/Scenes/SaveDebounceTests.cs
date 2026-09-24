using Vista.Core.Scenes;
using Xunit;

namespace Vista.Tests.Scenes;

public class SaveDebounceTests
{
    [Fact]
    public void TheSavedSceneIsNeverDue()
    {
        var saved = SceneEditing.New();
        var debounce = new SaveDebounce(saved);

        Assert.False(debounce.Due(saved, 0.0));
        Assert.False(debounce.Due(saved, 5.0));
    }

    [Fact]
    public void AChangedSceneIsDueOneSecondAfterItWasFirstSeen()
    {
        var saved = SceneEditing.New();
        var changed = saved with { PlaylistLoops = true };
        var debounce = new SaveDebounce(saved);

        // First seen at 10 s, so due from 10 + 1 = 11 s.
        Assert.False(debounce.Due(changed, 10.0));
        Assert.False(debounce.Due(changed, 10.9));
        Assert.True(debounce.Due(changed, 11.0));
    }

    [Fact]
    public void AnotherChangeRestartsTheWait()
    {
        var saved = SceneEditing.New();
        var first = saved with { PlaylistLoops = true };
        var second = first with { AnchorPlaced = true };
        var debounce = new SaveDebounce(saved);

        debounce.Due(first, 10.0);
        // The second scene arrives at 10.8 s, so it is due from 11.8 s, not 11 s.
        Assert.False(debounce.Due(second, 10.8));
        Assert.False(debounce.Due(second, 11.5));
        Assert.True(debounce.Due(second, 11.8));
    }

    [Fact]
    public void ASceneEqualByValueButNewIsAChange()
    {
        var saved = SceneEditing.New();
        var copy = saved with { };
        var debounce = new SaveDebounce(saved);

        debounce.Due(copy, 0.0);
        Assert.True(debounce.Due(copy, 1.0));

        // A value-equal copy of the pending scene arriving at 1.2 s restarts the wait: due from 2.2 s.
        var again = copy with
        { };
        Assert.False(debounce.Due(again, 1.2));
        Assert.False(debounce.Due(again, 2.0));
        Assert.True(debounce.Due(again, 2.2));
    }

    [Fact]
    public void SavingOrReturningToTheSavedSceneStopsItBeingDue()
    {
        var saved = SceneEditing.New();
        var changed = saved with { PlaylistLoops = true };
        var debounce = new SaveDebounce(saved);

        debounce.Due(changed, 0.0);
        Assert.False(debounce.Due(saved, 2.0));

        debounce.Due(changed, 3.0);
        Assert.True(debounce.Due(changed, 4.0));
        debounce.Saved(changed);
        Assert.False(debounce.Due(changed, 5.0));
    }
}
