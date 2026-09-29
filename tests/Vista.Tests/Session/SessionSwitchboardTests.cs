using Vista.Core.Scenes;
using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionSwitchboardTests
{
    // Live as OnAirScene leaves it: slot 0 on Program at 3 s, slot 1 Next, slots 1 and 2 resuming at 2 s and 4 s.
    private static readonly OnAir OnAirLive = new(0, 1, 3.0, Resume((1, 2.0), (2, 4.0)));

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

    // A session with OnAirScene open, in Edit.
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.LoadScene(OnAirScene());
        state.Edit();
        return state;
    }

    // Editing OnAirScene with Track 1 given points at x = 0 and 10 and put in Playlist 1, so Live has a shot to play.
    private static SessionState EditingPlayable()
    {
        var scene = OnAirScene();
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

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandWorksLiveWithoutAnUndoStep(string _, Func<SessionState, string?> command)
    {
        // One undo step (Auto Next on) before going live, where slot 0's Track 1 comes back on Program; the live command adds none, so one Undo empties the history.
        var state = EditingPlayable();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        state.Cue();
        Assert.True(state.Board!.HasProgram);
        var before = SlotsAndToggles(state);

        Assert.Null(command(state));

        var after = SlotsAndToggles(state);
        Assert.NotEqual(before, after);
        state.Edit();
        Assert.Equal(after, SlotsAndToggles(state));
        Assert.False(state.CanRedo);
        Assert.True(state.Undo());
        Assert.False(state.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void EachCommandIsRefusedOffAndInView(string _, Func<SessionState, string?> command)
    {
        var state = new SessionState();
        state.LoadScene(OnAirScene());
        var scene = state.Scene;

        Assert.Equal("The scene can only change while editing.", command(state));
        state.Release(CameraMode.View);
        Assert.Equal("The scene can only change while editing.", command(state));
        Assert.Same(scene, state.Scene);
    }

    [Fact]
    public void UndoKeepsLivesPosition()
    {
        var state = Editing();
        state.AssignSlot(3, state.Scene.Tracks[1].Id);

        state.Undo();

        Assert.Null(state.Scene.Switchboard.Slots[3]);
        SameAir(OnAirLive, state.Scene.Switchboard.Live);
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
        state.LoadScene(OnAirScene());

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
        CutTo(state.Board!, 1);
        Assert.False(state.OverlayEditable);

        // Track 2 is 2 s long, so 3 s holds its last frame.
        state.Transport.EditingFrame(3f, flying: false);
        Assert.True(state.Board.IsFinished);
        Assert.False(state.OverlayEditable);
        Assert.False(state.OverlayShown);

        state.Transport.EditingFrame(1f, flying: true);
        Assert.True(state.OverlayEditable);
    }
}
