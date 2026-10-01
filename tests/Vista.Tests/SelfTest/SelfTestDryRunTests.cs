#if DEBUG
using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.SelfTest;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.SelfTest;

public class SelfTestDryRunTests
{
    // Tracks "Opening" and "Pan", played in that order.
    private static Scene TwoEntries()
    {
        var (scene, second) = SceneEditing.Add(EmptyPlaylistScene());
        var first = scene.Tracks[0].Id;
        scene = SceneEditing.Rename(SceneEditing.Rename(scene, first, "Opening"), second, "Pan");
        return PlaylistEditing.Add(scene, [first, second]);
    }

    [Fact]
    public void NothingToPlayGivesNoShot() => Assert.Null(SelfTestDryRun.Shot([]));

    [Fact]
    public void TheShotPlaysEveryEntryOnceForOnePassWithoutLooping()
    {
        var track = TrackEditing.Empty();
        PlaylistItem[] items = [new(Guid.NewGuid(), track, null), new(Guid.NewGuid(), track, 3)];

        var shot = SelfTestDryRun.Shot(items)!;

        Assert.False(shot.Loops);
        Assert.Equal([items[0].EntryId, items[1].EntryId], shot.Items.Select(i => i.EntryId));
        Assert.All(shot.Items, item => Assert.Equal(1, item.Loops));
    }

    [Fact]
    public void EveryFrameGoodPasses()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);

        run.Check(Entries(scene)[0].Id, 0.0, WellFormedFrame, WellFormedFrame);
        run.Check(Entries(scene)[1].Id, 0.5, WellFormedFrame, WellFormedFrame);

        Assert.Equal(
            SelfTestResult.Pass("scene dry run", "2 frames well-formed and read back exactly"),
            run.Result(finished: true)
        );
    }

    [Fact]
    public void AMalformedFrameFailsNamingItsEntryTimeAndRule()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);
        // A 3 rad field of view is above the 120° (2.0944 rad) maximum.
        var wide = WellFormedFrame with
        {
            Fov = 3f,
        };

        run.Check(Entries(scene)[0].Id, 0.0, WellFormedFrame, WellFormedFrame);
        run.Check(Entries(scene)[1].Id, 1.25, wide, wide);
        run.Check(Entries(scene)[1].Id, 1.5, WellFormedFrame, WellFormedFrame);

        Assert.Equal(
            SelfTestResult.Fail(
                "scene dry run",
                "3 frames checked; first failure at entry 2 (Pan) at 1.25 s: the field of view is out of range (3)"
            ),
            run.Result(finished: true)
        );
    }

    [Fact]
    public void AFrameReadBackDifferentlyFails()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);
        // Written half a yalm higher than the game kept it; the position is the first part compared.
        var written = WellFormedFrame with
        {
            Position = new Vector3(1f, 2.5f, 3f),
            LookAt = new Vector3(1f, 2.5f, -7f),
        };

        run.Check(Entries(scene)[0].Id, 0.1, written, WellFormedFrame);

        Assert.Equal(
            "entry 1 (Opening) at 0.10 s: the position read back as (1, 2, 3), written (1, 2.5, 3)",
            run.FirstFailure
        );
    }

    [Fact]
    public void AFrameBelowTheGamesFieldOfViewFloorMayReadBackAsTheFloor()
    {
        var scene = TwoEntries();
        // The game's floor seen in game (2026-09-27) is 0.69: a 0.6 frame reads back as 0.69.
        var run = new SelfTestDryRun(scene, 0.69f);

        run.Check(Entries(scene)[0].Id, 0.1, WellFormedFrame with { Fov = 0.6f }, WellFormedFrame with { Fov = 0.69f });

        Assert.Null(run.FirstFailure);
    }

    [Fact]
    public void AMalformedFrameIsNamedForItsBrokenRuleBeforeItsReadBack()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);

        // Written with a 3 rad field of view, read back as 1 rad: both wrong, and the broken rule comes first.
        run.Check(Entries(scene)[0].Id, 0.0, WellFormedFrame with { Fov = 3f }, WellFormedFrame);

        Assert.Equal("entry 1 (Opening) at 0.00 s: the field of view is out of range (3)", run.FirstFailure);
    }

    [Fact]
    public void OnlyTheFirstFailureIsKeptButEveryFrameIsCounted()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);
        var wide = WellFormedFrame with { Fov = 3f };

        run.Check(Entries(scene)[0].Id, 0.0, wide, wide);
        run.Check(Entries(scene)[1].Id, 2.0, WellFormedFrame with { Fov = 0f }, WellFormedFrame);

        Assert.Equal(2, run.Frames);
        Assert.Equal("entry 1 (Opening) at 0.00 s: the field of view is out of range (3)", run.FirstFailure);
    }

    [Fact]
    public void StoppingBeforeTheEndFails()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene, 0f);
        run.Check(Entries(scene)[0].Id, 0.0, WellFormedFrame, WellFormedFrame);

        Assert.Equal(
            SelfTestResult.Fail("scene dry run", "1 frame checked; the camera hook stopped before the end"),
            run.Result(finished: false)
        );
    }

    [Fact]
    public void NoFramesCheckedFails() =>
        Assert.Equal(
            SelfTestResult.Fail("scene dry run", "no frames checked"),
            new SelfTestDryRun(TwoEntries(), 0f).Result(finished: true)
        );

    [Fact]
    public void SkippedSaysNothingCanPlay() =>
        Assert.Equal(SelfTestResult.Skip("scene dry run", "nothing in the playlist can play"), SelfTestDryRun.Skipped);
}
#endif
