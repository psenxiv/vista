using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class SwitchboardEditingTests
{
    [Fact]
    public void AnEmptySwitchboardHasTenEmptySlotsTogglesOffAndNothingOnAir()
    {
        var board = SwitchboardEditing.Empty();

        Assert.Equal(new Slot?[10], board.Slots);
        Assert.False(board.DirectCut);
        Assert.False(board.KeepRolling);
        Assert.False(board.AutoNext);
        SameAir(new OnAir(null, null, 0.0, new double?[10]), board.Live);
        SameAir(board.Live, SwitchboardEditing.EmptyAir());
    }

    [Fact]
    public void ASlotCanPlayATrackWithPointsOrAPlaylistWithAnEntryThatCan()
    {
        // Track 1 has points and Track 2 none; Playlist 1 holds only Track 2, Playlist 2 holds Track 1.
        var scene = TwoTracksTwoPlaylists();
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        scene = PlaylistEditing.Add(scene, [scene.Tracks[1].Id]);
        scene = PlaylistEditing.Add(PlaylistEditing.Select(scene, scene.Playlists[1].Id), [scene.Tracks[0].Id]);
        scene = SwitchboardEditing.Assign(scene, 0, scene.Tracks[0].Id);
        scene = SwitchboardEditing.Assign(scene, 1, scene.Tracks[1].Id);
        scene = SwitchboardEditing.Assign(scene, 2, scene.Playlists[0].Id);
        scene = SwitchboardEditing.Assign(scene, 3, scene.Playlists[1].Id);

        Assert.Equal(
            [false, true, false, false, true, false, false],
            new[] { -1, 0, 1, 2, 3, 4, 10 }.Select(slot => SwitchboardEditing.CanPlay(scene, slot))
        );
    }

    [Fact]
    public void ASlotLoopsWhenItsTrackHasLoopOnOrItsPlaylistHasLoopPlaylistOn()
    {
        // Track 1 has Loop on and Track 2 off; Playlist 1 has Loop playlist on and Playlist 2 off. Slot 4 is empty.
        var scene = TwoTracksTwoPlaylists();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[0], true));
        scene = PlaylistEditing.SetPlaylistLoops(PlaylistEditing.Select(scene, scene.Playlists[0].Id), true);
        scene = SwitchboardEditing.Assign(scene, 0, scene.Tracks[0].Id);
        scene = SwitchboardEditing.Assign(scene, 1, scene.Tracks[1].Id);
        scene = SwitchboardEditing.Assign(scene, 2, scene.Playlists[0].Id);
        scene = SwitchboardEditing.Assign(scene, 3, scene.Playlists[1].Id);

        Assert.Equal(
            [false, true, false, true, false, false, false],
            new[] { -1, 0, 1, 2, 3, 4, 10 }.Select(slot => SwitchboardEditing.Loops(scene, slot))
        );
    }

    [Fact]
    public void AssignLeavesTheSlotFollowingItsTrackOrPlaylistsName()
    {
        var scene = TwoTracksTwoPlaylists();
        var track = scene.Tracks[1].Id;
        var playlist = scene.Playlists[1].Id;

        scene = SwitchboardEditing.Assign(scene, 0, track);
        scene = SwitchboardEditing.Assign(scene, 9, playlist);

        // A following slot has no name of its own, and shows its track or playlist's.
        Assert.Equal(new Slot(null, track, null), scene.Switchboard.Slots[0]);
        Assert.Equal(new Slot(null, null, playlist), scene.Switchboard.Slots[9]);
        Assert.Equal("Track 2", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[0]!));
        Assert.Equal("Playlist 2", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[9]!));
        Assert.Equal(8, scene.Switchboard.Slots.Count(s => s is null));
    }

    [Fact]
    public void RenamingTheTargetLaterRenamesAFollowingSlotAndLeavesOneWithItsOwnName()
    {
        // Slots 0 and 1 follow Track 1 and Playlist 1; slot 2 is on Track 1 too, under its own name.
        var scene = TwoTracksTwoPlaylists();
        var track = scene.Tracks[0].Id;
        var playlist = scene.Playlists[0].Id;
        scene = SwitchboardEditing.Assign(scene, 0, track);
        scene = SwitchboardEditing.Assign(scene, 1, playlist);
        scene = SwitchboardEditing.Rename(SwitchboardEditing.Assign(scene, 2, track), 2, "Wide");

        scene = SceneEditing.Rename(scene, track, "Dolly");
        scene = PlaylistEditing.Rename(scene, playlist, "Opening");

        Assert.Equal("Dolly", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[0]!));
        Assert.Equal("Opening", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[1]!));
        Assert.Equal("Wide", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[2]!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void ASlotOutOfRangeIsRefused(int slot)
    {
        var scene = OnAirScene();

        Assert.Throws<ArgumentException>(() => SwitchboardEditing.Assign(scene, slot, scene.Tracks[0].Id));
        Assert.Throws<ArgumentException>(() => SwitchboardEditing.Rename(scene, slot, "Wide"));
        Assert.Throws<ArgumentException>(() => SwitchboardEditing.Clear(scene, slot));
    }

    [Fact]
    public void AssigningAnIdTheSceneDoesntHoldIsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() =>
            SwitchboardEditing.Assign(TwoTracksTwoPlaylists(), 0, Guid.NewGuid())
        );
        Assert.Equal("There is no such track or playlist.", refused.Message);
    }

    [Fact]
    public void RenameTrimsAndAllowsANameAnotherSlotHas()
    {
        // Slot 2 holds Track 2, so "Track 1" is a name of its own; slot 0 follows Track 1 and shows the same.
        var scene = SwitchboardEditing.Rename(OnAirScene(), 2, "  Track 1  ");

        Assert.Equal("Track 1", scene.Switchboard.Slots[2]!.Name);
        Assert.Equal("Track 1", SwitchboardEditing.NameOf(scene, scene.Switchboard.Slots[0]!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Track 1")]
    [InlineData("  Track 1  ")]
    public void RenamingToABlankNameOrTheTargetsOwnPutsTheSlotBackToFollowing(string name)
    {
        // Slot 0 holds Track 1 under its own name, "Wide".
        var scene = SwitchboardEditing.Rename(OnAirScene(), 0, "Wide");
        Assert.Equal("Wide", scene.Switchboard.Slots[0]!.Name);

        var renamed = SwitchboardEditing.Rename(scene, 0, name);

        Assert.Equal(new Slot(null, scene.Tracks[0].Id, null), renamed.Switchboard.Slots[0]);
        Assert.Equal("Track 1", SwitchboardEditing.NameOf(renamed, renamed.Switchboard.Slots[0]!));
    }

    [Fact]
    public void RenamingAPlaylistSlotToThePlaylistsOwnNameLeavesItFollowing()
    {
        // Slot 1 follows Playlist 1, so naming it "Playlist 1" changes nothing.
        var scene = OnAirScene();

        Assert.Same(scene, SwitchboardEditing.Rename(scene, 1, "Playlist 1"));
    }

    [Fact]
    public void RenameRefusesATooLongNameAndAnEmptySlot()
    {
        var scene = OnAirScene();

        // SceneNames.MaxLength is 100, after trimming: 101 characters is one too many, 100 inside spaces is fine.
        Assert.Equal(
            "That name is too long.",
            Assert.Throws<ArgumentException>(() => SwitchboardEditing.Rename(scene, 0, new string('a', 101))).Message
        );
        Assert.Equal(
            new string('a', 100),
            SwitchboardEditing.Rename(scene, 0, $" {new string('a', 100)} ").Switchboard.Slots[0]!.Name
        );
        Assert.Equal(
            "That slot is empty.",
            Assert.Throws<ArgumentException>(() => SwitchboardEditing.Rename(scene, 3, "Wide")).Message
        );
        Assert.Equal(
            "That slot is empty.",
            Assert.Throws<ArgumentException>(() => SwitchboardEditing.Rename(scene, 3, "")).Message
        );
    }

    [Fact]
    public void RenameChangesNothingOnAir()
    {
        var scene = OnAirScene();

        var renamed = SwitchboardEditing.Rename(scene, 0, "Wide");

        Assert.Equal("Wide", renamed.Switchboard.Slots[0]!.Name);
        SameAir(scene.Switchboard.Live, renamed.Switchboard.Live);
    }

    [Fact]
    public void ClearingTheProgramSlotEmptiesProgramAndItsResume()
    {
        var scene = OnAirScene();

        var cleared = SwitchboardEditing.Clear(scene, 0);

        Assert.Null(cleared.Switchboard.Slots[0]);
        // Program leaves slot 0, so nothing is on Program and its time goes back to 0; Next and the other resumes stay.
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), cleared.Switchboard.Live);
    }

    [Fact]
    public void ClearingTheNextSlotEmptiesNextAndItsResume()
    {
        var cleared = SwitchboardEditing.Clear(OnAirScene(), 1);

        SameAir(new OnAir(0, null, 3.0, Resume((2, 4.0))), cleared.Switchboard.Live);
    }

    [Fact]
    public void ClearingAnEmptySlotChangesNothing()
    {
        var scene = OnAirScene();

        Assert.Same(scene, SwitchboardEditing.Clear(scene, 5));
    }

    [Fact]
    public void AssigningTheProgramSlotAnotherTargetTakesProgramOffIt()
    {
        var scene = OnAirScene();

        var assigned = SwitchboardEditing.Assign(scene, 0, scene.Tracks[1].Id);

        Assert.Equal(new Slot(null, scene.Tracks[1].Id, null), assigned.Switchboard.Slots[0]);
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), assigned.Switchboard.Live);
    }

    [Fact]
    public void AssigningTheNextSlotAnotherTargetKeepsNextButClearsItsResume()
    {
        var scene = OnAirScene();

        var assigned = SwitchboardEditing.Assign(scene, 1, scene.Playlists[1].Id);

        SameAir(new OnAir(0, 1, 3.0, Resume((2, 4.0))), assigned.Switchboard.Live);
    }

    [Fact]
    public void AssigningASlotItsOwnTargetAgainKeepsItOnAirAndPutsItBackToFollowing()
    {
        var scene = SwitchboardEditing.Rename(OnAirScene(), 0, "Wide");

        var assigned = SwitchboardEditing.Assign(scene, 0, scene.Tracks[0].Id);

        Assert.Equal(new Slot(null, scene.Tracks[0].Id, null), assigned.Switchboard.Slots[0]);
        SameAir(scene.Switchboard.Live, assigned.Switchboard.Live);
    }

    [Fact]
    public void AssigningOverASlotWithItsOwnNameLeavesItFollowingTheNewTarget()
    {
        // Slot 0 holds Track 1 as "Wide"; pointed at Playlist 2 it shows "Playlist 2".
        var scene = SwitchboardEditing.Rename(OnAirScene(), 0, "Wide");

        var assigned = SwitchboardEditing.Assign(scene, 0, scene.Playlists[1].Id);

        Assert.Equal(new Slot(null, null, scene.Playlists[1].Id), assigned.Switchboard.Slots[0]);
        Assert.Equal("Playlist 2", SwitchboardEditing.NameOf(assigned, assigned.Switchboard.Slots[0]!));
    }

    [Fact]
    public void TheHintSaysHowToFillAnEmptySlotAndThatAHeldOneCantPlay()
    {
        // Track 1 has points and Track 2 none; Playlist 1 is empty, Playlist 2 holds only Track 2. Slot 5 is empty.
        var scene = TwoTracksTwoPlaylists();
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        scene = PlaylistEditing.Add(PlaylistEditing.Select(scene, scene.Playlists[1].Id), [scene.Tracks[1].Id]);
        scene = SwitchboardEditing.Assign(scene, 0, scene.Tracks[0].Id);
        scene = SwitchboardEditing.Assign(scene, 1, scene.Tracks[1].Id);
        scene = SwitchboardEditing.Assign(scene, 2, scene.Playlists[0].Id);
        scene = SwitchboardEditing.Assign(scene, 3, scene.Playlists[1].Id);

        Assert.Equal(
            [null, "Nothing to play yet", "Nothing to play yet", "Nothing to play yet", "Right-click to assign"],
            new[] { 0, 1, 2, 3, 5 }.Select(slot => SwitchboardEditing.Hint(scene, slot))
        );
    }

    [Theory]
    [InlineData(SwitchboardToggle.DirectCut, true, false, false)]
    [InlineData(SwitchboardToggle.KeepRolling, false, true, false)]
    [InlineData(SwitchboardToggle.AutoNext, false, false, true)]
    public void SetToggleSetsOnlyThatToggle(SwitchboardToggle toggle, bool direct, bool rolling, bool auto)
    {
        var on = SwitchboardEditing.SetToggle(OnAirScene(), toggle, true);
        var off = SwitchboardEditing.SetToggle(on, toggle, false).Switchboard;

        Assert.Equal(
            (direct, rolling, auto),
            (on.Switchboard.DirectCut, on.Switchboard.KeepRolling, on.Switchboard.AutoNext)
        );
        Assert.Equal((false, false, false), (off.DirectCut, off.KeepRolling, off.AutoNext));
    }

    [Fact]
    public void SetEnabledTurnsUseSwitchboardOnAndOffAndLeavesTheRestAlone()
    {
        // OnAirScene never sets it, so it starts off.
        var scene = OnAirScene();
        Assert.False(scene.Switchboard.Enabled);

        var on = SwitchboardEditing.SetEnabled(scene, true);
        Assert.True(on.Switchboard.Enabled);
        SameBoard(scene.Switchboard with { Enabled = true }, on.Switchboard);

        var off = SwitchboardEditing.SetEnabled(on, false);
        Assert.False(off.Switchboard.Enabled);
        SameBoard(scene.Switchboard, off.Switchboard);
    }

    [Fact]
    public void SettingUseSwitchboardToWhatItIsChangesNothing()
    {
        var off = OnAirScene();
        var on = SwitchboardEditing.SetEnabled(off, true);

        Assert.Same(off, SwitchboardEditing.SetEnabled(off, false));
        Assert.Same(on, SwitchboardEditing.SetEnabled(on, true));
    }

    [Fact]
    public void SettingAToggleToWhatItIsChangesNothing()
    {
        var scene = OnAirScene();

        Assert.Same(scene, SwitchboardEditing.SetToggle(scene, SwitchboardToggle.AutoNext, false));
    }

    [Fact]
    public void ForgetEmptiesEverySlotOnTheIdAndFollowsOnAir()
    {
        // Slot 3 also on Track 1, so forgetting Track 1 empties slots 0 and 3; Program was on slot 0.
        var scene = OnAirScene();
        scene = SwitchboardEditing.Assign(scene, 3, scene.Tracks[0].Id);

        var forgotten = SwitchboardEditing.Forget(scene, scene.Tracks[0].Id);

        Assert.Null(forgotten.Switchboard.Slots[0]);
        Assert.Null(forgotten.Switchboard.Slots[3]);
        Assert.NotNull(forgotten.Switchboard.Slots[1]);
        Assert.NotNull(forgotten.Switchboard.Slots[2]);
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), forgotten.Switchboard.Live);
    }

    [Fact]
    public void FollowTakesProgramAndNextOffAnEmptiedSlotAndClearsItsResume()
    {
        var before = OnAirScene().Switchboard.Slots;
        var after = before.ToArray();
        after[0] = null;
        after[1] = null;

        // Both on-air slots emptied: Program and Next leave them, their resumes clear, slot 2's stays.
        var air = SwitchboardEditing.Follow(new OnAir(0, 1, 3.0, Resume((0, 1.0), (1, 2.0), (2, 4.0))), before, after);

        SameAir(new OnAir(null, null, 0.0, Resume((2, 4.0))), air);
    }

    [Fact]
    public void FollowTakesProgramOffARetargetedSlotButKeepsNextOnOne()
    {
        var before = OnAirScene().Switchboard.Slots;
        var after = before.ToArray();
        after[0] = before[2];
        after[1] = before[2];

        // Slot 0 (Program) and slot 1 (Next) now hold Track 2: Program leaves, Next stays, both resumes clear.
        var air = SwitchboardEditing.Follow(new OnAir(0, 1, 3.0, Resume((0, 1.0), (1, 2.0), (2, 4.0))), before, after);

        SameAir(new OnAir(null, 1, 0.0, Resume((2, 4.0))), air);
    }

    [Fact]
    public void FollowIgnoresARenameAndAnUntouchedSlot()
    {
        var before = OnAirScene().Switchboard.Slots;
        var after = before.ToArray();
        after[0] = before[0]! with { Name = "Wide" };
        var air = new OnAir(0, 1, 3.0, Resume((0, 1.0), (1, 2.0)));

        Assert.Same(air, SwitchboardEditing.Follow(air, before, after));
    }
}
