using Vista.Core.Editing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionSwitchboardTests
{
    public static TheoryData<string, Func<SessionState, string?>> Commands =>
        new()
        {
            { "assign", s => s.AssignSlot(3, s.Scene.Tracks[1].Id) },
            { "rename", s => s.RenameSlot(0, "Wide") },
            { "clear", s => s.ClearSlot(2) },
            { "toggle", s => s.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true) },
        };

    // The slots and toggles, as one comparable value.
    private static (string Slots, bool, bool, bool) SlotsAndToggles(SessionState state)
    {
        var board = state.Scene.Switchboard;
        return (string.Join("|", board.Slots), board.DirectCut, board.KeepRolling, board.AutoNext);
    }

    // OnAirScene with Use switchboard on.
    private static Scene OnAirSceneInUse() => SwitchboardEditing.SetEnabled(OnAirScene(), true);

    // A session with OnAirScene open, in Edit, using its switchboard unless told not to.
    private static SessionState Editing(bool on = true)
    {
        var state = new SessionState();
        state.LoadScene(SwitchboardEditing.SetEnabled(OnAirScene(), on));
        state.Edit();
        return state;
    }

    // Editing OnAirScene with Track 1 given points at x = 0 and 10, a 2 s shot, and put in Playlist 1, so Live has a shot to play; it uses its switchboard unless told not to.
    private static SessionState EditingPlayable(bool on = true)
    {
        var scene = SwitchboardEditing.SetEnabled(OnAirScene(), on);
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        var state = new SessionState();
        state.LoadScene(scene);
        state.Edit();
        return state;
    }

    // A toggle changing the scene pins that the scene comparison behind undo steps sees the switchboard.
    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsOneUndoStepInEdit(string _, Func<SessionState, string?> command)
    {
        var state = Editing();
        var before = SlotsAndToggles(state);

        Assert.Null(command(state));

        Assert.NotEqual(before, SlotsAndToggles(state));
        Assert.True(state.CanUndo);
        Assert.True(state.Undo());
        Assert.Equal(before, SlotsAndToggles(state));
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void RenamingASlotToABlankNameIsOneUndoStepInEditAndUndoBringsItsOwnNameBack()
    {
        // Slot 0 holds Track 1 as "Wide"; loading the scene starts the undo history empty.
        var state = new SessionState();
        state.LoadScene(SwitchboardEditing.Rename(OnAirSceneInUse(), 0, "Wide"));
        state.Edit();

        Assert.Null(state.RenameSlot(0, ""));

        Assert.Null(state.Scene.Switchboard.Slots[0]!.Name);
        Assert.True(state.CanUndo);
        Assert.True(state.Undo());
        Assert.Equal("Wide", state.Scene.Switchboard.Slots[0]!.Name);
        Assert.False(state.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsOneUndoStepInLive(string _, Func<SessionState, string?> command)
    {
        // Auto Next on is a step to undo and Direct cut on, undone, one to redo; going live, slot 0's Track 1 comes back on Program and plays.
        var state = EditingPlayable();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        state.SetSwitchboardToggle(SwitchboardToggle.DirectCut, true);
        state.Undo();
        state.Cue();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        var before = SlotsAndToggles(state);

        Assert.Null(command(state));

        var after = SlotsAndToggles(state);
        Assert.NotEqual(before, after);
        Assert.False(state.CanUndo);
        Assert.False(state.Undo());
        Assert.Equal(after, SlotsAndToggles(state));
        Assert.True(state.IsPlaying);
        state.Edit();
        Assert.False(state.CanRedo);
        Assert.True(state.Undo());
        Assert.Equal(before, SlotsAndToggles(state));
        Assert.True(state.Undo());
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void UndoInEditWalksBackThroughLiveSwitchboardChangesThenEditChanges()
    {
        var state = EditingPlayable();
        var track2 = TrackId(state, 1);
        var playlist2 = state.Scene.Playlists[1].Id;
        state.SetTrackSpeed(2f);
        state.Cue();
        state.AssignSlot(3, track2);
        state.AssignSlot(4, playlist2);
        state.RenameSlot(3, "Wide");
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        state.Edit();
        var board = state.Scene.Switchboard;

        Assert.True(state.Undo());
        board = board with { AutoNext = false };
        SameBoard(board, state.Scene.Switchboard);

        Assert.True(state.Undo());
        board = board with { Slots = ListEdit.Replace(board.Slots, 3, new Slot(null, track2, null)) };
        SameBoard(board, state.Scene.Switchboard);

        Assert.True(state.Undo());
        board = board with { Slots = ListEdit.Replace(board.Slots, 4, null) };
        SameBoard(board, state.Scene.Switchboard);

        Assert.True(state.Undo());
        board = board with { Slots = ListEdit.Replace(board.Slots, 3, null) };
        SameBoard(board, state.Scene.Switchboard);
        Assert.Equal(2f, state.StoredTrack.Speed);

        Assert.True(state.Undo());
        Assert.Equal(TrackEditing.DefaultSpeed, state.StoredTrack.Speed);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void ALiveCommandThatChangesNothingIsNoUndoStep()
    {
        // Slot 0 already holds Track 1 under its name, slot 5 is empty and Direct cut is off.
        var state = EditingPlayable();
        state.Cue();

        Assert.Null(state.AssignSlot(0, TrackId(state, 0)));
        Assert.Null(state.RenameSlot(0, "Track 1"));
        Assert.Null(state.ClearSlot(5));
        Assert.Null(state.SetSwitchboardToggle(SwitchboardToggle.DirectCut, false));
        state.Edit();

        Assert.False(state.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsRefusedOffAndInView(string _, Func<SessionState, string?> command)
    {
        // The mode is why, whether or not the scene uses its switchboard.
        foreach (var on in new[] { true, false })
        {
            var state = new SessionState();
            state.LoadScene(SwitchboardEditing.SetEnabled(OnAirScene(), on));
            var scene = state.Scene;

            Assert.Equal("The switchboard can only change in Edit or Live.", command(state));
            state.Release(CameraMode.View);
            Assert.Equal("The switchboard can only change in Edit or Live.", command(state));
            Assert.Same(scene, state.Scene);
        }
    }

    public static TheoryData<string, Func<SessionState, string?>> EveryCommand =>
        new()
        {
            { "assign", s => s.AssignSlot(3, s.Scene.Tracks[1].Id) },
            { "rename", s => s.RenameSlot(0, "Wide") },
            { "clear", s => s.ClearSlot(2) },
            { "direct cut", s => s.SetSwitchboardToggle(SwitchboardToggle.DirectCut, true) },
            { "keep rolling", s => s.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true) },
            { "auto next", s => s.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true) },
        };

    [Theory]
    [MemberData(nameof(EveryCommand))]
    public void EachCommandIsRefusedInEditAndLiveWhileTheSceneDoesntUseItsSwitchboard(
        string _,
        Func<SessionState, string?> command
    )
    {
        var state = EditingPlayable(on: false);
        var scene = state.Scene;

        Assert.Equal("Turn on Use switchboard first.", command(state));
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal("Turn on Use switchboard first.", command(state));

        Assert.Same(scene, state.Scene);
        state.Edit();
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void ASceneThatDoesntUseItsSwitchboardHasNoneToDriveOrShow()
    {
        // The scene saves slot 0 on Program and slot 1 Next, which aren't shown while it is off.
        var state = EditingPlayable(on: false);
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.Null(state.Board);
        Assert.Equal<(int?, int?)>((null, null), state.ShownAir);

        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Null(state.Board);
        Assert.Equal<(int?, int?)>((null, null), state.ShownAir);

        state.Release();
        Assert.Equal<(int?, int?)>((null, null), state.ShownAir);
        Assert.Equal(0, state.Scene.Switchboard.Live.Program);
        Assert.Equal(1, state.Scene.Switchboard.Live.Next);
    }

    [Fact]
    public void TheBoardsPlayPauseAndRestartLeaveLivesPlaylistAlone()
    {
        // Live plays the playlist's 2 s shot, paused 1 s in.
        var state = EditingPlayable(on: false);
        GoLive(state);
        state.LiveFrame(1f);
        state.Stop();

        Assert.False(state.CanPlayBoard);
        Assert.Equal(PlayOutcome.Refused, state.PlayBoard());
        Assert.Equal(PlayOutcome.Refused, state.RestartBoard());
        state.LiveFrame(0.5f);
        Assert.False(state.IsPlaying);
        Assert.Equal(1.0, state.LivePlaylist!.Head, 1e-5);

        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.False(state.BoardPlaying);
        state.PauseBoard();
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void TurningUseSwitchboardOnInEditGivesABoardWithNothingOnProgramOrNext()
    {
        var state = EditingPlayable(on: false);

        Assert.Null(state.SetUseSwitchboard(true));

        Assert.NotNull(state.Board);
        Assert.Null(state.Board.Program);
        Assert.Null(state.Board.Next);
        Assert.Equal<(int?, int?)>((null, null), state.ShownAir);
    }

    [Fact]
    public void TurningUseSwitchboardOnLeavesTheEditedTracksPreviewPlaying()
    {
        var state = EditingTwoPoints();
        Assert.Equal(PlayOutcome.Previewed, state.Play());

        Assert.Null(state.SetUseSwitchboard(true));

        Assert.True(state.Transport.Previewing);
    }

    public static TheoryData<string, Action<SessionState>, Action<SessionState>> WaysToTurnItOff =>
        new()
        {
            { "command", _ => { }, s => s.SetUseSwitchboard(false) },
            {
                "undo",
                s =>
                {
                    s.SetUseSwitchboard(false);
                    s.SetUseSwitchboard(true);
                },
                s => s.Undo()
            },
            {
                "redo",
                s =>
                {
                    s.SetUseSwitchboard(false);
                    s.Undo();
                },
                s => s.Redo()
            },
        };

    [Theory]
    [MemberData(nameof(WaysToTurnItOff))]
    public void TurningUseSwitchboardOffStopsEditsCutAndFliesOnFromItsLastFrame(
        string _,
        Action<SessionState> prepare,
        Action<SessionState> turnOff
    )
    {
        var state = EditingSwitchboard();
        prepare(state);
        var board = state.Board!;
        CutTo(board, 0);
        // 1 s of Track 1, x = 2t: x = 2.
        Assert.Equal(2f, state.Transport.EditingFrame(1f, flying: false).Shown!.Value.Position.X, 1e-3f);

        turnOff(state);

        Assert.False(state.Scene.Switchboard.Enabled);
        Assert.Null(state.Board);
        Assert.False(board.HasProgram);
        Assert.True(state.OverlayEditable);
        var next = state.Transport.EditingFrame(1f, flying: false);
        Assert.Null(next.Shown);
        Assert.Equal(2f, next.FlyFrom!.Value.Position.X, 1e-3f);
        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void LoadingASceneInEditGivesABoardOnlyWhenItUsesItsSwitchboard()
    {
        // Slot 1 holds Playlist 1, which can play, so a click makes it Next.
        var state = EditingPlayable();
        state.Board!.Click(1);
        Assert.Equal(1, state.Board.Next);

        state.LoadScene(OnAirScene());
        Assert.Null(state.Board);

        state.LoadScene(OnAirSceneInUse());
        Assert.NotNull(state.Board);
        Assert.Null(state.Board.Program);
        Assert.Null(state.Board.Next);
    }

    [Fact]
    public void EnteringEditGivesNoBoardWhileTheSceneDoesntUseItsSwitchboard()
    {
        var state = new SessionState();
        state.LoadScene(OnAirScene());

        state.Edit();

        Assert.Null(state.Board);
    }

    [Fact]
    public void DeletingTheProgramSlotsTrackEmptiesItWhileTheSwitchboardIsOff()
    {
        // Slot 0 holds Track 1 and is on Program at 3 s; slot 1 is Next, and slots 1 and 2 resume at 2 s and 4 s.
        var state = Editing(on: false);

        Assert.Null(state.DeleteTracks([state.Scene.Tracks[0].Id]));

        Assert.Null(state.Scene.Switchboard.Slots[0]);
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), state.Scene.Switchboard.Live);
    }

    [Fact]
    public void WithTheSwitchboardInUseLiveShowsNoPlaylistAndPlayWhilePlayingOnlyReHides()
    {
        // The playlist's 2 s shot is cut to on slot 0.
        var state = EditingTwoPoints();
        state.AddToPlaylist([state.EditedTrackId]);
        CutLive(state);
        Assert.True(state.IsPlaying);
        Assert.Null(state.LivePlaylist);
        Assert.Null(state.PlayingEntry);

        state.LiveFrame(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.Equal(1.0, state.Board!.Head, 1e-5);
    }

    [Fact]
    public void WithTheSwitchboardInUseStopPausesTheProgramShot()
    {
        // The playlist's 2 s shot is cut to on slot 0 and stopped 1 s in; a further 0.5 s leaves the head at 1 s.
        var state = EditingTwoPoints();
        state.AddToPlaylist([state.EditedTrackId]);
        CutLive(state);
        state.LiveFrame(1f);

        Assert.True(state.Stop());

        Assert.False(state.IsPlaying);
        state.LiveFrame(0.5f);
        Assert.Equal(1.0, state.Board!.Head, 1e-5);
    }

    [Fact]
    public void HoveringASlotSaysToTurnUseSwitchboardOnWhileItIsOff()
    {
        // Slot 0 holds a track and slot 5 is empty.
        var state = new SessionState();
        state.LoadScene(OnAirScene());
        Assert.Equal("Switchboard is off", state.SlotHint(0));
        Assert.Equal("Switchboard is off", state.SlotHint(5));

        state.Edit();
        Assert.Equal("Switchboard is off", state.SlotHint(0));
        Assert.Equal("Switchboard is off", state.SlotHint(5));
    }

    [Fact]
    public void HoveringASlotGivesItsHintOnlyWhereTheSwitchboardCanBeDriven()
    {
        // Slot 0 holds Track 1, which has no points, and slot 5 is empty.
        var state = new SessionState();
        state.LoadScene(OnAirSceneInUse());
        Assert.Null(state.SlotHint(0));
        Assert.Null(state.SlotHint(5));

        state.Release(CameraMode.View);
        Assert.Null(state.SlotHint(0));
        Assert.Null(state.SlotHint(5));

        state.Edit();
        Assert.Equal("Nothing to play yet", state.SlotHint(0));
        Assert.Equal("Right-click to assign", state.SlotHint(5));
    }

    [Fact]
    public void UseSwitchboardIsRefusedOutsideEditAndChangesNothing()
    {
        var state = EditingPlayable(on: false);
        state.Release();

        void Refused(CameraMode mode)
        {
            Assert.Equal(mode, state.Mode);
            var scene = state.Scene;
            Assert.False(state.CanSetUseSwitchboard);
            Assert.Equal("Use switchboard can only change in Edit.", state.SetUseSwitchboard(true));
            Assert.Same(scene, state.Scene);
            Assert.False(state.Scene.Switchboard.Enabled);
        }

        Refused(CameraMode.Off);
        state.Release(CameraMode.View);
        Refused(CameraMode.View);
        state.Cue();
        Refused(CameraMode.Live);
        state.Edit();
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void UseSwitchboardsTooltipSaysWhereToChangeItOutsideEdit()
    {
        var state = EditingPlayable();
        Assert.True(state.CanSetUseSwitchboard);
        Assert.Equal("Use switchboard", state.UseSwitchboardTooltip);

        state.Cue();
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.Equal("Use switchboard (toggle in Edit)", state.UseSwitchboardTooltip);
    }

    [Fact]
    public void UseSwitchboardIsOneUndoStepInEditAndUndoAndRedoRestoreIt()
    {
        // OnAirScene has it off, so setting it off is no step.
        var state = Editing(on: false);
        Assert.Null(state.SetUseSwitchboard(false));
        Assert.False(state.CanUndo);

        Assert.Null(state.SetUseSwitchboard(true));
        Assert.True(state.Scene.Switchboard.Enabled);
        Assert.True(state.CanUndo);

        Assert.True(state.Undo());
        Assert.False(state.Scene.Switchboard.Enabled);
        Assert.False(state.CanUndo);

        Assert.True(state.Redo());
        Assert.True(state.Scene.Switchboard.Enabled);
        Assert.False(state.CanRedo);
    }

    [Fact]
    public void LoadingASceneKeepsWhetherItUsesTheSwitchboard()
    {
        var state = new SessionState();

        Assert.Null(state.LoadScene(SwitchboardEditing.SetEnabled(OnAirScene(), true)));
        Assert.True(state.Scene.Switchboard.Enabled);

        Assert.Null(state.LoadScene(OnAirScene()));
        Assert.False(state.Scene.Switchboard.Enabled);
    }

    [Fact]
    public void UndoKeepsLivesPosition()
    {
        // The Assign's snapshot has nothing on Program or Next; Live then puts slot 0's Track 1 on Program, leaves it 3 s in, and makes slot 1 Next.
        var state = EditingSwitchboard();
        state.AssignSlot(3, TrackId(state, 1));
        state.Cue();
        CutTo(state.Board!, 0);
        state.LiveFrame(3f);
        state.Board!.Click(1);
        state.Edit();

        Assert.True(state.Undo());

        var live = state.Scene.Switchboard.Live;
        Assert.Equal(0, live.Program);
        Assert.Equal(3.0, live.ProgramTime, 1e-9);
        Assert.Equal(1, live.Next);
    }

    [Fact]
    public void UndoDoesntPutBackWhatAChangeTookOffAir()
    {
        // Clearing slot 0 takes Program off it; undo brings the slot back but keeps Live as it now is.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[0];
        state.ClearSlot(0);

        state.Undo();

        Assert.Equal(slot, state.Scene.Switchboard.Slots[0]);
        SameAir(new OnAir(null, 1, 0.0, Resume((1, 2.0), (2, 4.0))), state.Scene.Switchboard.Live);
    }

    [Fact]
    public void UndoingATrackDeletionBringsItsSlotsBack()
    {
        // Slot 0 is on Track 1.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[0];
        state.DeleteTracks([state.Scene.Tracks[0].Id]);

        state.Undo();
        Assert.Equal(slot, state.Scene.Switchboard.Slots[0]);
    }

    [Fact]
    public void UndoingAPlaylistDeletionBringsItsSlotsBack()
    {
        // Slot 1 is on Playlist 1.
        var state = Editing();
        var slot = state.Scene.Switchboard.Slots[1];
        state.DeletePlaylist(state.Scene.Playlists[0].Id);

        state.Undo();
        Assert.Equal(slot, state.Scene.Switchboard.Slots[1]);
    }

    [Fact]
    public void ARefusedCommandIsNoUndoStep()
    {
        var state = Editing();

        Assert.Equal("That slot is empty.", state.RenameSlot(5, "Wide"));
        Assert.False(state.CanUndo);
    }

    // EditingSwitchboard with one step to undo (Keep rolling on) and one to redo (Auto Next on), then slot 0's Track 1 (x = 2t) cut to and shown for 1 s, with slot 1 Next.
    private static (SessionState State, SwitchboardPlayer Board) EditPreviewing()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        state.Undo();
        var board = state.Board!;
        CutTo(board, 0);
        board.Click(1);
        state.Transport.EditingFrame(1f, flying: false);
        Assert.True(board.HasProgram);
        return (state, board);
    }

    public static TheoryData<string, Action<SessionState>> PreviewStoppers =>
        new()
        {
            { "play", s => s.Play() },
            { "restart", s => s.Restart() },
            { "track edit", s => s.SetTrackSpeed(3f) },
            { "added point", s => s.AddToEnd(Point(30f)) },
            { "scene edit", s => s.RenameTrack(TrackId(s, 0), "Wide") },
            { "undo", s => s.Undo() },
            { "redo", s => s.Redo() },
            { "scrub", s => s.Transport.BeginScrub() },
            { "scrub to", s => s.Transport.ScrubTo(1.0) },
            { "jump to point", s => s.JumpToPoint(1) },
        };

    [Theory]
    [MemberData(nameof(PreviewStoppers))]
    public void EditsSwitchboardPreviewStopsAndEmptiesProgram(string _, Action<SessionState> stop)
    {
        var (state, board) = EditPreviewing();

        stop(state);

        Assert.Null(board.Program);
        Assert.False(board.HasProgram);
        Assert.Equal(1, board.Next);
    }

    public static TheoryData<string, Func<SessionState, string?>> PreviewKeepers =>
        new()
        {
            { "toggle", s => s.SetSwitchboardToggle(SwitchboardToggle.DirectCut, true) },
            { "rename the program slot", s => s.RenameSlot(0, "Wide") },
            { "assign another slot", s => s.AssignSlot(5, TrackId(s, 1)) },
            { "clear another slot", s => s.ClearSlot(3) },
            { "point the next slot elsewhere", s => s.AssignSlot(1, TrackId(s, 0)) },
            { "switch track", s => s.SwitchTrack(TrackId(s, 1)) },
        };

    [Theory]
    [MemberData(nameof(PreviewKeepers))]
    public void SwitchboardCommandsLeaveEditsPreviewPlaying(string _, Func<SessionState, string?> command)
    {
        var (state, board) = EditPreviewing();

        Assert.Null(command(state));

        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);
        // 1 s more of Track 1, 2 s in: x = 4.
        Assert.Equal(4f, state.Transport.EditingFrame(1f, flying: false).Shown!.Value.Position.X, 1e-3f);
    }

    public static TheoryData<string, Func<SessionState, string?>> TrackPreviewKeepers =>
        new()
        {
            { "assign", s => s.AssignSlot(5, TrackId(s, 1)) },
            { "toggle", s => s.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true) },
            { "use switchboard", s => s.SetUseSwitchboard(!s.Scene.Switchboard.Enabled) },
        };

    [Theory]
    [MemberData(nameof(TrackPreviewKeepers))]
    public void SwitchboardCommandsLeaveTheEditedTracksPreviewPlaying(string _, Func<SessionState, string?> command)
    {
        var state = EditingSwitchboard();
        Assert.Equal(PlayOutcome.Previewed, state.Play());
        Assert.True(state.Transport.Previewing);

        Assert.Null(command(state));

        Assert.True(state.Transport.Previewing);
    }

    [Fact]
    public void ALiveEditStopsEditsSwitchboardPreviewOnlyOnceItCommits()
    {
        var (state, board) = EditPreviewing();
        state.BeginLiveEdit();
        state.EndLiveEdit();
        Assert.True(board.HasProgram);

        // 2 yalms is off the 1.3 yalm default, so the edit ends as a step.
        state.BeginLiveEdit();
        state.PreviewAimHeight(2f);
        Assert.True(board.HasProgram);
        state.EndLiveEdit();

        Assert.False(board.HasProgram);
    }

    [Fact]
    public void ClearingEditsNextSlotEmptiesNextAndClearingItsProgramSlotStopsThePreview()
    {
        var (state, board) = EditPreviewing();

        state.ClearSlot(1);
        Assert.Null(board.Next);
        Assert.True(board.HasProgram);

        state.ClearSlot(0);
        Assert.Null(board.Program);
        Assert.False(board.HasProgram);
    }

    [Fact]
    public void PointingEditsProgramSlotElsewhereStopsThePreview()
    {
        var (state, board) = EditPreviewing();

        state.AssignSlot(0, TrackId(state, 1));

        Assert.Null(board.Program);
        Assert.False(board.HasProgram);
    }

    [Fact]
    public void UndoKeepsEditsNextInStepWithTheSlots()
    {
        // Slot 5 is empty until the Assign, so undoing it takes Next off slot 5.
        var state = EditingSwitchboard();
        state.AssignSlot(5, TrackId(state, 1));
        state.Board!.Click(5);

        state.Undo();

        Assert.Null(state.Board.Next);
    }

    [Fact]
    public void EditsBoardStartsEmptyEachTimeEditStarts()
    {
        var (state, board) = EditPreviewing();

        state.Release();
        Assert.Null(state.Board);
        state.Edit();
        var fresh = state.Board!;
        Assert.NotSame(board, fresh);
        Assert.Null(fresh.Program);
        Assert.Null(fresh.Next);

        fresh.Click(1);
        state.Cue();
        state.Edit();
        Assert.NotSame(fresh, state.Board);
        Assert.Null(state.Board!.Next);
    }

    [Fact]
    public void TheSwitchboardShowsTheSavedLiveProgramAndNextOnlyInOffAndView()
    {
        var state = new SessionState();
        state.LoadScene(OnAirSceneInUse());

        // OnAirScene saves slot 0 on Program and slot 1 Next.
        Assert.Equal<(int?, int?)>((0, 1), state.ShownAir);
        state.Edit();
        // Edit's own board starts empty.
        Assert.Equal<(int?, int?)>((null, null), state.ShownAir);
        state.Release(CameraMode.View);
        // Edit left the saved state alone.
        Assert.Equal<(int?, int?)>((0, 1), state.ShownAir);
        state.Cue();
        // Slot 0's Track 1 has no points, so Live empties Program on entry and keeps Next.
        Assert.Equal<(int?, int?)>((null, 1), state.ShownAir);
    }

    [Fact]
    public void LoadingASceneEmptiesEditsBoard()
    {
        var (state, _) = EditPreviewing();

        state.LoadScene(state.Scene);

        Assert.Null(state.Board!.Program);
        Assert.Null(state.Board.Next);
        Assert.False(state.Board.HasProgram);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ACutInEditStopsTheEditedTracksPreview(bool ghost, bool directCut)
    {
        var state = EditingSwitchboard();
        state.Transport.SetGhost(ghost);
        state.SetSwitchboardToggle(SwitchboardToggle.DirectCut, directCut);
        state.Play();

        state.Board!.Click(1);
        state.Board.Cut();

        Assert.Equal(1, state.Board.Program);
        Assert.False(state.Transport.Previewing);
        Assert.False(state.IsPlaying);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOverlayHidesWhileEditsSwitchboardPreviewIsOnTheCamera(bool ghost)
    {
        var state = EditingSwitchboard();
        state.Transport.SetGhost(ghost);
        var board = state.Board!;
        CutTo(board, 1);
        Assert.False(state.OverlayEditable);

        // Track 2 is 2 s long, so 3 s holds its last frame.
        state.Transport.EditingFrame(3f, flying: false);
        Assert.True(board.IsFinished);
        Assert.False(state.OverlayEditable);
        Assert.False(state.OverlayShown);

        state.Transport.EditingFrame(1f, flying: true);
        Assert.True(state.OverlayEditable);
    }

    [Fact]
    public void EditsBoardPausesAndPlaysOnFromThePause()
    {
        var state = EditingSwitchboard();
        var board = state.Board!;
        CutTo(board, 0);
        state.Transport.EditingFrame(1f, flying: false);

        state.PauseBoard();
        state.Transport.EditingFrame(1f, flying: false);
        // Paused 1 s into Track 1, so another second leaves the head at 1 s.
        Assert.False(state.BoardPlaying);
        Assert.True(board.HasProgram);
        Assert.Equal(1.0, board.Head, 1e-6);

        Assert.Equal(PlayOutcome.Previewed, state.PlayBoard());
        state.Transport.EditingFrame(1f, flying: false);
        // On from 1 s for 1 s: 2 s.
        Assert.True(state.BoardPlaying);
        Assert.Equal(2.0, board.Head, 1e-6);
    }

    [Fact]
    public void PlayingEditsBoardAfterItsShotEndsStartsItAgain()
    {
        var state = EditingSwitchboard();
        var board = state.Board!;
        CutTo(board, 1);
        // Track 2 is 2 s long, so 3 s holds it at its end.
        state.Transport.EditingFrame(3f, flying: false);
        Assert.True(board.IsFinished);

        state.PlayBoard();
        state.Transport.EditingFrame(0.5f, flying: false);
        // From its start again for 0.5 s.
        Assert.Equal(0.5, board.Head, 1e-6);
    }

    [Fact]
    public void RestartingEditsBoardPlaysItsShotFromTheStart()
    {
        var state = EditingSwitchboard();
        var board = state.Board!;
        CutTo(board, 0);
        state.Transport.EditingFrame(4f, flying: false);
        state.PauseBoard();

        Assert.Equal(PlayOutcome.Previewed, state.RestartBoard());
        Assert.Equal(0.0, board.Head, 1e-6);
        Assert.True(state.BoardPlaying);
    }

    [Fact]
    public void TheBoardsPlayAndRestartAreRefusedWithNothingOnProgram()
    {
        var state = EditingSwitchboard();
        Assert.False(state.CanPlayBoard);
        Assert.Equal(PlayOutcome.Refused, state.PlayBoard());
        Assert.Equal(PlayOutcome.Refused, state.RestartBoard());

        // Off has no switchboard, and the board's Play doesn't go Live from there.
        state.Release();
        Assert.False(state.CanPlayBoard);
        Assert.Equal(PlayOutcome.Refused, state.PlayBoard());
        Assert.Equal(CameraMode.Off, state.Mode);
    }

    [Fact]
    public void LivesBoardPausesPlaysAndRestartsTheProgramShot()
    {
        var state = EditingSwitchboard();
        state.Cue();
        var board = state.Board!;
        CutTo(board, 0);
        state.LiveFrame(1f);

        state.PauseBoard();
        state.LiveFrame(1f);
        // Paused 1 s into Track 1.
        Assert.False(state.BoardPlaying);
        Assert.Equal(1.0, board.Head, 1e-6);

        Assert.Equal(PlayOutcome.Resumed, state.PlayBoard());
        state.LiveFrame(1f);
        Assert.Equal(2.0, board.Head, 1e-6);

        Assert.Equal(PlayOutcome.Started, state.RestartBoard());
        Assert.Equal(0.0, board.Head, 1e-6);
    }
}
