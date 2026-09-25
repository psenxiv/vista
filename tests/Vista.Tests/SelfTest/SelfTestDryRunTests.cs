#if DEBUG
using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.SelfTest;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Playback;
using Xunit;

namespace Vista.Tests.SelfTest;

public class SelfTestDryRunTests
{
    // At (1, 2, 3) looking 10 yalms along -Z with up +Y and a 1 rad field of view: well-formed.
    private static readonly CameraState Good = new(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, -7f),
        Vector3.UnitY,
        1f
    );

    // Tracks "Opening" and "Pan", played in that order.
    private static Scene TwoEntries()
    {
        var (scene, second) = SceneEditing.Add(SceneEditing.New());
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
        var run = new SelfTestDryRun(scene);

        run.Check(scene.Playlist[0].Id, 0.0, Good, Good);
        run.Check(scene.Playlist[1].Id, 0.5, Good, Good);

        Assert.Equal(
            SelfTestResult.Pass("scene dry run", "2 frames well-formed and read back exactly"),
            run.Result(finished: true)
        );
    }

    [Fact]
    public void AMalformedFrameFailsNamingItsEntryTimeAndRule()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene);
        // A 3 rad field of view is above the 120° (2.0944 rad) maximum.
        var wide = Good with
        {
            Fov = 3f,
        };

        run.Check(scene.Playlist[0].Id, 0.0, Good, Good);
        run.Check(scene.Playlist[1].Id, 1.25, wide, wide);
        run.Check(scene.Playlist[1].Id, 1.5, Good, Good);

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
        var run = new SelfTestDryRun(scene);
        // Written half a yalm higher than the game kept it; the position is the first part compared.
        var written = Good with
        {
            Position = new Vector3(1f, 2.5f, 3f),
            LookAt = new Vector3(1f, 2.5f, -7f),
        };

        run.Check(scene.Playlist[0].Id, 0.1, written, Good);

        Assert.Equal(
            "entry 1 (Opening) at 0.10 s: the position read back as (1, 2, 3), written (1, 2.5, 3)",
            run.FirstFailure
        );
    }

    [Fact]
    public void AMalformedFrameIsNamedForItsBrokenRuleBeforeItsReadBack()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene);

        // Written with a 3 rad field of view, read back as 1 rad: both wrong, and the broken rule comes first.
        run.Check(scene.Playlist[0].Id, 0.0, Good with { Fov = 3f }, Good);

        Assert.Equal("entry 1 (Opening) at 0.00 s: the field of view is out of range (3)", run.FirstFailure);
    }

    [Fact]
    public void OnlyTheFirstFailureIsKeptButEveryFrameIsCounted()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene);
        var wide = Good with { Fov = 3f };

        run.Check(scene.Playlist[0].Id, 0.0, wide, wide);
        run.Check(scene.Playlist[1].Id, 2.0, Good with { Fov = 0f }, Good);

        Assert.Equal(2, run.Frames);
        Assert.Equal("entry 1 (Opening) at 0.00 s: the field of view is out of range (3)", run.FirstFailure);
    }

    [Fact]
    public void StoppingBeforeTheEndFails()
    {
        var scene = TwoEntries();
        var run = new SelfTestDryRun(scene);
        run.Check(scene.Playlist[0].Id, 0.0, Good, Good);

        Assert.Equal(
            SelfTestResult.Fail("scene dry run", "1 frame checked; the camera hook stopped before the end"),
            run.Result(finished: false)
        );
    }

    [Fact]
    public void NoFramesCheckedFails() =>
        Assert.Equal(
            SelfTestResult.Fail("scene dry run", "no frames checked"),
            new SelfTestDryRun(TwoEntries()).Result(finished: true)
        );

    [Fact]
    public void SkippedSaysNothingCanPlay() =>
        Assert.Equal(SelfTestResult.Skip("scene dry run", "nothing in the playlist can play"), SelfTestDryRun.Skipped);
}
#endif
