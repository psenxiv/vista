using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SwitchboardPlayerTests
{
    // Seconds and yalms read off a frame, to within the arc-length table's resolution.
    private const float Along = 1e-3f;

    // Seconds through a shot, as the playhead counts them.
    private const double Time = 1e-9;

    // The frame the game camera showed as Live began: nowhere on any track.
    private static readonly CameraState Start = new(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, -7f),
        Vector3.UnitY,
        1f
    );

    // Enters Live from the camera at Start.
    private static SwitchboardPlayer Live(SessionState state)
    {
        state.Cue(Start);
        return state.Board!;
    }

    private static OnAir Air(SessionState state) => state.Scene.Switchboard.Live;

    // The camera's x after dt seconds more of Live.
    private static float XAfter(SessionState state, float dt) => state.LiveFrame(dt)!.Value.Position.X;

    [Fact]
    public void ClickingASlotMakesItNextAndPlaysNothing()
    {
        var state = EditingSwitchboard();
        var board = Live(state);

        board.Click(0);

        Assert.Equal(0, board.Next);
        Assert.Null(board.Program);
        Assert.False(board.HasProgram);
        Assert.Equal(Start, state.LiveFrame(1f));
    }

    [Fact]
    public void TheProgramSlotCanBeMadeNext()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);

        board.Click(0);

        Assert.Equal(0, board.Program);
        Assert.Equal(0, board.Next);
    }

    [Fact]
    public void DirectCutCutsStraightAwayAndLeavesNextAlone()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        board.Click(0);
        state.SetSwitchboardToggle(SwitchboardToggle.DirectCut, true);

        board.Click(1);

        Assert.Equal(1, board.Program);
        Assert.Equal(0, board.Next);
        Assert.True(board.IsPlaying);
        // Track 2 from its start: 1 s on is x = 2.
        Assert.Equal(2f, XAfter(state, 1f), Along);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEmptyOrUnplayableSlotDoesNothing(bool directCut)
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        board.Click(1);
        state.SetSwitchboardToggle(SwitchboardToggle.DirectCut, directCut);

        board.Click(5);
        board.Click(2);

        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);
        Assert.False(board.CanPlay(5));
        Assert.False(board.CanPlay(2));
        Assert.True(board.CanPlay(3));
    }

    [Fact]
    public void CutWithNextEmptyDoesNothing()
    {
        var state = EditingSwitchboard();
        var board = Live(state);

        board.Cut();
        Assert.Null(board.Program);

        CutTo(board, 0);
        state.LiveFrame(3f);
        board.Cut();

        Assert.Equal(0, board.Program);
        Assert.Equal(3.0, board.Head, Time);
    }

    [Fact]
    public void CutEmptiesNextAndPlaysEvenFromPaused()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.Stop();
        board.Click(1);

        board.Cut();

        Assert.Equal(1, board.Program);
        Assert.Null(board.Next);
        Assert.False(board.IsPaused);
        Assert.True(board.IsPlaying);
        Assert.Equal(0.0, board.Head, Time);
    }

    [Fact]
    public void KeepRollingResumesAShotWhereItWasCutAwayFrom()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(4f);
        CutTo(board, 1);
        state.LiveFrame(1f);

        CutTo(board, 0);

        // Track 1 left at 4 s, Track 2 at 1 s.
        Assert.Equal(4.0, board.Head, Time);
        Assert.Equal(Resume((0, 4.0), (1, 1.0)), Air(state).Resume);
        // 1 s on from 4 s is 5 s: x = 10.
        Assert.Equal(10f, XAfter(state, 1f), Along);
    }

    [Fact]
    public void WithoutKeepRollingCuttingBackStartsAtZero()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(4f);
        CutTo(board, 1);

        CutTo(board, 0);

        Assert.Equal(0.0, board.Head, Time);
        // The cuts still record where each shot was left: Track 1 at 4 s, Track 2 at its start.
        Assert.Equal(Resume((0, 4.0), (1, 0.0)), Air(state).Resume);
    }

    [Fact]
    public void KeepRollingResumesFromTheLastCutAwayEvenOneMadeWithItOff()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(4f);
        CutTo(board, 1);
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, false);
        CutTo(board, 0);
        state.LiveFrame(8f);
        CutTo(board, 1);
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);

        CutTo(board, 0);

        // Track 1 was last left at 8 s, having started again from 0 with Keep rolling off.
        Assert.Equal(8.0, board.Head, Time);
        // 1 s on from 8 s is 9 s: x = 18.
        Assert.Equal(18f, XAfter(state, 1f), Along);
    }

    [Fact]
    public void KeepRollingStartsAFinishedShotFromItsBeginning()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var board = Live(state);
        CutTo(board, 1);
        // Track 2 runs 2 s, so 3 s on it has finished.
        state.LiveFrame(3f);
        Assert.True(board.IsFinished);
        CutTo(board, 0);
        Assert.Null(Air(state).Resume[1]);

        CutTo(board, 1);

        Assert.Equal(0.0, board.Head, Time);
        Assert.False(board.IsFinished);
    }

    [Fact]
    public void CuttingFromTheProgramSlotToItselfResumesWhereItWas()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(4f);

        CutTo(board, 0);

        Assert.Equal(4.0, board.Head, Time);
    }

    [Fact]
    public void KeepRollingRestartsAShotShortenedPastItsResumePoint()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(8f);
        CutTo(board, 1);
        state.Edit();
        // Twice the speed: Track 1 now runs 20 yalms in 5 s, short of its 8 s resume point.
        state.SetTrackSpeed(4f);
        board = Live(state);

        CutTo(board, 0);

        Assert.Equal(0.0, board.Head, Time);
        Assert.False(board.IsFinished);
    }

    [Fact]
    public void ALoopingTrackKeepsLoopingEvenWithAutoNextAndNextSet()
    {
        var state = EditingSwitchboard();
        state.ChangeTrack(t => TrackEditing.SetLoop(t, true));
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 0);
        board.Click(1);

        state.LiveFrame(13f);

        // 13 s round a 10 s loop is 3 s into its second pass.
        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);
        Assert.False(board.IsFinished);
        Assert.Equal(3.0, board.Head, Time);
    }

    [Fact]
    public void APlainTrackHoldsItsLastFrame()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 1);

        // Track 2 ends at x = 4 after 2 s, and holds there.
        Assert.Equal(4f, XAfter(state, 3f), Along);
        Assert.Equal(4f, XAfter(state, 1f), Along);
        Assert.True(board.IsFinished);
        Assert.Equal(1, board.Program);
    }

    [Fact]
    public void AutoNextCutsToNextAtTheShotsEnd()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 1);
        board.Click(0);

        state.LiveFrame(3f);

        Assert.Equal(0, board.Program);
        Assert.Null(board.Next);
        Assert.Equal(0.0, board.Head, Time);
        // Track 1 from its start: 1 s on is x = 2.
        Assert.Equal(2f, XAfter(state, 1f), Along);
    }

    [Fact]
    public void AutoNextWaitsWhileTheHeadIsHeldAtTheEnd()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 1);
        board.Click(0);

        // Dragged to Track 2's 2 s end and held there: finished, but paused.
        board.BeginScrub();
        board.ScrubTo(2.0);
        state.LiveFrame(1f);
        Assert.True(board.IsFinished);
        Assert.Equal(1, board.Program);

        // Let go at 1.5 s: 1 s of playing reaches the end, and Auto Next cuts.
        board.ScrubTo(1.5);
        board.EndScrub();
        state.LiveFrame(1f);
        Assert.Equal(0, board.Program);
    }

    [Fact]
    public void AutoNextDoesntCutAShotLetGoAtItsEnd()
    {
        // A scrub that lets go at the very end leaves the shot there; it never plays into its end.
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 1);
        board.Click(0);

        board.BeginScrub();
        board.ScrubTo(2.0);
        board.EndScrub();
        state.LiveFrame(1f);
        state.LiveFrame(1f);

        Assert.Equal(1, board.Program);
        Assert.Equal(0, board.Next);
    }

    [Fact]
    public void ClickingNextWhileAShotHoldsAtItsEndDoesntCut()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 1);
        // Track 2 ends at 2 s with Next empty, and holds.
        state.LiveFrame(3f);

        board.Click(0);
        state.LiveFrame(1f);
        state.LiveFrame(1f);

        Assert.Equal(1, board.Program);
        Assert.Equal(0, board.Next);
        Assert.True(board.IsFinished);
    }

    [Fact]
    public void TurningAutoNextOnWhileAShotHoldsAtItsEndDoesntCut()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 1);
        board.Click(0);
        // Track 2 ends at 2 s with Auto Next off, and holds.
        state.LiveFrame(3f);

        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        state.LiveFrame(1f);
        state.LiveFrame(1f);

        Assert.Equal(1, board.Program);
        Assert.Equal(0, board.Next);
    }

    [Fact]
    public void AutoNextHoldsWithNextEmpty()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 1);

        Assert.Equal(4f, XAfter(state, 3f), Along);

        Assert.Equal(1, board.Program);
        Assert.True(board.IsFinished);
    }

    [Fact]
    public void AutoNextAndCutHoldWithANextThatCantPlay()
    {
        // Track 1 on Program at 3 s and Track 2 Next; then Edit takes Track 2's points away.
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(3f);
        board.Click(1);
        state.Edit();
        state.SwitchTrack(TrackId(state, 1));
        state.DeletePoints([0, 1]);
        board = Live(state);

        board.Cut();
        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);

        state.Play();
        // 3 s in, 7 s more reaches Track 1's end at x = 20, which holds.
        Assert.Equal(20f, XAfter(state, 8f), Along);
        Assert.Equal(20f, XAfter(state, 1f), Along);
        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);
    }

    [Fact]
    public void AutoNextOnAPlaylistSlotWaitsForThePlaylistsEnd()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 3);
        board.Click(0);

        // 3 s is past Track 2's 2 s entry, 1 s into Track 1's.
        state.LiveFrame(3f);
        Assert.Equal(3, board.Program);
        Assert.Equal(1, board.EntryIndex);
        Assert.Equal(3.0, board.Head, Time);

        // 10 s more is 13 s, past the playlist's 12.
        state.LiveFrame(10f);
        Assert.Equal(0, board.Program);
        Assert.Equal(0.0, board.Head, Time);
    }

    [Fact]
    public void ALoopingPlaylistSlotKeepsLooping()
    {
        var state = EditingSwitchboard();
        state.SetPlaylistLoops(true);
        state.SetSwitchboardToggle(SwitchboardToggle.AutoNext, true);
        var board = Live(state);
        CutTo(board, 3);
        board.Click(0);

        state.LiveFrame(13f);

        // 13 s round the 12 s playlist is 1 s into Track 2's entry again.
        Assert.Equal(3, board.Program);
        Assert.Equal(0, board.EntryIndex);
        Assert.Equal(1.0, board.Head, Time);
    }

    [Fact]
    public void ClearingTheProgramSlotLiveStopsTheShotAndHoldsItsFrame()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        var frame = state.LiveFrame(2.5f);
        // 2.5 s into Track 1 is x = 5.
        Assert.Equal(5f, frame!.Value.Position.X, Along);

        Assert.Null(state.ClearSlot(0));

        Assert.Equal(frame, state.LiveFrame(1f));
        Assert.Equal(frame, state.LiveFrame(1f));
        Assert.Null(board.Program);
        Assert.False(board.HasProgram);
        Assert.Null(board.Timeline);
        Assert.False(state.CanStart);
    }

    [Fact]
    public void TheFirstLiveHoldsTheFrameItBeganAt()
    {
        var state = EditingSwitchboard();

        Live(state);

        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.Null(state.Board!.Program);
        Assert.Equal(Start, state.LiveFrame(1f));
        Assert.Equal(Start, state.LiveFrame(1f));
    }

    [Fact]
    public void LiveHoldsNothingWhenTheCameraCouldntBeRead()
    {
        var state = EditingSwitchboard();

        state.Cue(null);

        Assert.Null(state.LiveFrame(1f));
    }

    [Fact]
    public void ReenteringLiveResumesTheProgramShotPausedWithNextKept()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(3f);
        board.Click(1);

        state.Edit();
        Assert.Equal(3.0, Air(state).ProgramTime, Time);
        board = Live(state);

        Assert.Equal(0, board.Program);
        Assert.Equal(1, board.Next);
        Assert.True(board.IsPaused);
        Assert.Equal(3.0, board.Head, Time);
        // Paused at 3 s: x = 6, however long passes.
        Assert.Equal(6f, XAfter(state, 1f), Along);
        Assert.Equal(3.0, board.Head, Time);
    }

    [Fact]
    public void ReenteringLiveStartsAFinishedProgramShotFromItsBeginning()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 1);
        state.LiveFrame(3f);
        state.Release();

        board = Live(state);

        Assert.Equal(1, board.Program);
        Assert.False(board.IsFinished);
        Assert.True(board.IsPaused);
        Assert.Equal(0.0, board.Head, Time);
    }

    [Fact]
    public void AProgramThatCantPlayAnyMoreEmptiesOnEnteringLive()
    {
        var state = EditingSwitchboard();
        CutTo(Live(state), 1);
        state.Edit();
        state.SwitchTrack(TrackId(state, 1));
        state.DeletePoints([0, 1]);

        var board = Live(state);

        Assert.Null(board.Program);
        Assert.Null(Air(state).Program);
        Assert.Equal(Start, state.LiveFrame(1f));
    }

    [Fact]
    public void LeavingLiveByAnyPathStoresWhereTheProgramShotHadGot()
    {
        // Edit, Release and a fault each leave Live 3 s into Track 1.
        foreach (
            var leave in new Action<SessionState>[] { s => s.Edit(), s => s.Release(), s => s.ReportFault("test") }
        )
        {
            var state = EditingSwitchboard();
            CutTo(Live(state), 0);
            state.LiveFrame(3f);

            leave(state);

            Assert.False(state.Board is { HasProgram: true });
            Assert.Equal(3.0, Air(state).ProgramTime, Time);
        }
    }

    [Fact]
    public void PlayFromViewEntersLiveAndPlaysTheProgramShot()
    {
        var state = EditingSwitchboard();
        CutTo(Live(state), 0);
        state.LiveFrame(3f);
        state.Release(CameraMode.View);

        Assert.Equal(PlayOutcome.StartedFromGame, state.Play(Start));

        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.IsPlaying);
        // From 3 s, 1 s on is 4 s: x = 8.
        Assert.Equal(8f, XAfter(state, 1f), Along);
    }

    [Fact]
    public void PlayFromViewWithProgramEmptyEntersLiveAndHolds()
    {
        var state = EditingSwitchboard();
        state.Release(CameraMode.View);

        Assert.Equal(PlayOutcome.CuedFromGame, state.Play(Start));

        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.False(state.IsPlaying);
        Assert.Equal(Start, state.LiveFrame(1f));
    }

    [Fact]
    public void PlayInLiveRestartsAFinishedProgramShot()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 1);
        state.LiveFrame(3f);

        Assert.Equal(PlayOutcome.Started, state.Play());

        Assert.Equal(0.0, board.Head, Time);
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void RestartPlaysTheProgramShotFromItsStart()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(4f);
        state.Stop();

        Assert.Equal(PlayOutcome.Started, state.Restart());

        Assert.Equal(0.0, board.Head, Time);
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void NothingStartsInLiveWithProgramEmpty()
    {
        var state = EditingSwitchboard();
        Live(state);

        Assert.False(state.CanStart);
        Assert.False(state.CanRestart);
        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.False(state.Stop());
        Assert.True(state.CanGoLive);
    }

    [Fact]
    public void ClickAndCutAreNeverUndoSteps()
    {
        var state = EditingSwitchboard();
        var board = Live(state);

        CutTo(board, 0);
        board.Click(1);
        state.Edit();

        Assert.False(state.CanUndo);
        Assert.Equal(0, Air(state).Program);
        Assert.Equal(1, Air(state).Next);
    }

    [Fact]
    public void AnUndoThatTakesTheProgramShotOffItsSlotEmptiesProgram()
    {
        // Slot 0 on Track 1 goes on Program, then slot 0 is pointed at Track 2 (an undo step, which takes it off Program) and cut to again.
        // The undo step's snapshot has Track 1 on slot 0 with Program 0; undoing it must not bring Program back onto a slot now holding another shot.
        var state = EditingSwitchboard();
        CutTo(Live(state), 0);
        state.Edit();
        Assert.Null(state.AssignSlot(0, TrackId(state, 1)));
        CutTo(Live(state), 0);
        state.Edit();

        Assert.True(state.Undo());

        Assert.Equal(TrackId(state, 0), state.Scene.Switchboard.Slots[0]!.TrackId);
        Assert.Null(Air(state).Program);
        Assert.Equal(0.0, Air(state).ProgramTime);
        Assert.Null(Live(state).Program);
    }

    [Fact]
    public void ScrubbingPausesThenCarriesOnPlaying()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 3);

        board.BeginScrub();
        Assert.True(board.IsPaused);
        Assert.True(board.Scrubbing);
        // 5 s through the playlist is 3 s into Track 1's entry, after Track 2's 2 s.
        board.ScrubTo(5.0);
        Assert.Equal(1, board.EntryIndex);
        Assert.Equal(5.0, board.Head, Time);
        board.EndScrub();

        Assert.False(board.Scrubbing);
        Assert.True(board.IsPlaying);
        // 3 s into Track 1, 1 s on: x = 8.
        Assert.Equal(8f, XAfter(state, 1f), Along);
    }

    [Fact]
    public void ScrubbingAPausedShotLeavesItPaused()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.Stop();

        board.BeginScrub();
        board.ScrubTo(2.0);
        board.EndScrub();

        Assert.True(board.IsPaused);
        Assert.Equal(2.0, board.Head, Time);
    }

    [Fact]
    public void ScrubbingAFinishedShotBackUnfinishesIt()
    {
        var state = EditingSwitchboard();
        var board = Live(state);
        CutTo(board, 0);
        state.LiveFrame(20f);
        Assert.True(board.IsFinished);

        board.BeginScrub();
        board.ScrubTo(3.0);
        board.EndScrub();

        Assert.False(board.IsFinished);
        Assert.True(board.IsPlaying);
    }

    [Fact]
    public void EachModeHasItsOwnBoardAndOffAndViewHaveNone()
    {
        var state = EditingSwitchboard();
        var edit = state.Board;
        Assert.NotNull(edit);
        Assert.Null(state.LiveFrame(1f));

        Assert.NotSame(edit, Live(state));
        state.Release();
        Assert.Null(state.Board);
        state.Release(CameraMode.View);
        Assert.Null(state.Board);
    }

    // The Edit camera's x after dt seconds more of Edit's switchboard preview.
    private static float EditXAfter(SessionState state, float dt) =>
        state.Transport.EditingFrame(dt, flying: false).Shown!.Value.Position.X;

    [Fact]
    public void KeepRollingDoesNothingOnEditsBoard()
    {
        var state = EditingSwitchboard();
        state.SetSwitchboardToggle(SwitchboardToggle.KeepRolling, true);
        var live = Air(state);
        var board = state.Board!;
        CutTo(board, 0);
        // Track 1 is x = 2t: 4 s in, x = 8.
        Assert.Equal(8f, EditXAfter(state, 4f), Along);

        CutTo(board, 1);
        CutTo(board, 0);

        // Live would resume at 4 s; Edit starts again from 0, so 1 s on is x = 2.
        Assert.Equal(2f, EditXAfter(state, 1f), Along);
        Assert.Same(live, Air(state));
    }

    [Fact]
    public void EditsClicksAndCutsLeaveLiveAndTheUndoHistoryAlone()
    {
        var state = EditingSwitchboard();
        var live = Air(state);
        var board = state.Board!;

        CutTo(board, 0);
        CutTo(board, 3);
        board.Click(1);
        state.Transport.EditingFrame(1f, flying: false);

        Assert.Equal(3, board.Program);
        Assert.Equal(1, board.Next);
        Assert.Same(live, Air(state));
        Assert.False(state.CanUndo);
        var onAir = Live(state);
        Assert.Null(onAir.Program);
        Assert.Null(onAir.Next);
    }
}
