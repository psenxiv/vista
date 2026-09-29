using System.Numerics;
using Vista.Core.Session;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

// EditingThreePoints runs x = 2t over 10 s, so each frame's x is twice its shot time.
public class SessionPreviewToolsTests
{
    private const string GhostOnlyInEdit = "The ghost camera is only for Edit.";
    private const string RateOnlyInEdit = "The playback rate is only for Edit previews.";
    private const string RateOutOfRange = "The playback rate must be from 0.01 to 2.";

    [Fact]
    public void TheGhostTurnsOnAndOffInEditAndStaysOnThroughLive()
    {
        var state = EditingThreePoints();
        Assert.False(state.Transport.Ghost);
        Assert.Null(state.Transport.SetGhost(true));
        Assert.True(state.Transport.Ghost);

        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        Assert.Equal(GhostOnlyInEdit, state.Transport.SetGhost(false));
        Assert.True(state.Transport.Ghost);

        state.Edit();
        Assert.True(state.Transport.Ghost);
        Assert.Null(state.Transport.SetGhost(false));
        Assert.False(state.Transport.Ghost);
    }

    [Fact]
    public void TheGhostIsRefusedInOff()
    {
        var state = new SessionState();

        Assert.Equal(GhostOnlyInEdit, state.Transport.SetGhost(true));
        Assert.False(state.Transport.Ghost);
    }

    [Fact]
    public void TheRateIsSetInEditUpToTwoAndStaysThroughLive()
    {
        var state = EditingThreePoints();
        Assert.Equal(1f, state.Transport.PlaybackRate);
        Assert.Null(state.Transport.SetPlaybackRate(2f));
        Assert.Equal(2f, state.Transport.PlaybackRate);

        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        Assert.Equal(RateOnlyInEdit, state.Transport.SetPlaybackRate(1f));
        Assert.Equal(2f, state.Transport.PlaybackRate);

        state.Edit();
        Assert.Equal(2f, state.Transport.PlaybackRate);
    }

    [Fact]
    public void TheSlowestRateIsAHundredthOfNormalSpeed()
    {
        var state = EditingThreePoints();

        Assert.Null(state.Transport.SetPlaybackRate(0.01f));
        Assert.Equal(0.01f, state.Transport.PlaybackRate);
    }

    [Theory]
    [InlineData(0.009f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(2.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ARateOutsideItsRangeIsRefusedAndLeavesTheRate(float rate)
    {
        var state = EditingThreePoints();
        state.Transport.SetPlaybackRate(0.5f);

        Assert.Equal(RateOutOfRange, state.Transport.SetPlaybackRate(rate));
        Assert.Equal(0.5f, state.Transport.PlaybackRate);
    }

    [Theory]
    [InlineData(1.04f, 1f)] // 10.4 tenths rounds to 10
    [InlineData(1.06f, 1.1f)] // 10.6 tenths rounds to 11
    [InlineData(0.26f, 0.3f)] // 2.6 tenths rounds to 3
    [InlineData(1.94f, 1.9f)] // 19.4 tenths rounds to 19
    [InlineData(2f, 2f)] // already a tenth, and the fastest
    [InlineData(0.01f, 0.01f)] // the far left rounds to 0, below the slowest, so it clamps back to 0.01
    [InlineData(0.04f, 0.01f)] // 0.4 tenths rounds to 0, which clamps to 0.01
    [InlineData(0.06f, 0.1f)] // 0.6 tenths rounds to 1
    [InlineData(2.3f, 2f)] // 23 tenths is past the fastest, so it clamps to 2
    public void ADraggedRateSnapsToTheNearestTenthWithinRange(float dragged, float expected)
    {
        Assert.Equal(expected, Transport.SnapPlaybackRate(dragged), 1e-6f);
    }

    [Fact]
    public void APreviewPlaysAtTheRate()
    {
        var state = EditingThreePoints();
        state.Transport.SetPlaybackRate(0.5f);
        state.Play();

        state.Transport.AdvancePreview(1f);

        // 1 s at half speed is 0.5 s of the shot.
        Assert.Equal(0.5, state.Transport.ScrubHead, 5);
    }

    [Fact]
    public void APreviewShowsOnTheGameCamera()
    {
        var state = EditingThreePoints();
        state.Play();

        var frame = state.Transport.EditingFrame(1f, flying: false);

        // 1 s in: x = 2.
        Assert.Equal(2f, frame.Shown!.Value.Position.X, 1e-3f);
        Assert.Null(frame.FlyFrom);
    }

    [Fact]
    public void FlyingStopsAPreviewAndHandsTheFreeCamItsLastFrameOnce()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        var frame = state.Transport.EditingFrame(1f, flying: true);

        Assert.False(state.Transport.Previewing);
        Assert.Null(frame.Shown);
        // The last frame shown was 1 s in: x = 2.
        Assert.Equal(2f, frame.FlyFrom!.Value.Position.X, 1e-3f);
        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void StoppingAPreviewHandsTheFreeCamItsLastFrame()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(3f, flying: false);
        state.Stop();

        var frame = state.Transport.EditingFrame(1f, flying: false);

        // Stopped 3 s in: x = 6.
        Assert.Null(frame.Shown);
        Assert.Equal(6f, frame.FlyFrom!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void APreviewReachingItsEndHandsTheFreeCamTheLastPoint()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(9f, flying: false);

        var frame = state.Transport.EditingFrame(5f, flying: false);

        // 14 s is past the 10 s track, so it finishes on the last point: x = 20.
        Assert.False(state.Transport.Previewing);
        Assert.Null(frame.Shown);
        Assert.Equal(20f, frame.FlyFrom!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void ScrubbingInEditShowsTheScrubbedFrameAndTheFreeCamOtherwise()
    {
        var state = EditingThreePoints();
        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(4.0);

        // 4 s: x = 8.
        Assert.Equal(8f, state.Transport.EditingFrame(1f, flying: false).Shown!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void WithTheGhostAPreviewPlaysOnTheGhostAndFlyingLeavesItPlaying()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);
        state.Play();

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: true));

        Assert.True(state.Transport.Previewing);
        // 1 s in: x = 2.
        Assert.Equal(2f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void AGhostPreviewStoppingHandsNothingOffAndTheGhostStaysAtTheScrubHead()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);
        state.Play();
        state.Transport.EditingFrame(3f, flying: false);
        state.Stop();

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
        // Stopped 3 s in, where the scrub head stays: x = 6.
        Assert.Equal(6f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);

        state.Transport.ScrubTo(1.0);

        // Scrubbed to 1 s: x = 2, not the stopped preview's x = 6.
        Assert.Equal(2f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void TurningTheGhostOnMidPreviewGivesTheViewBackToTheFreeCam()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        state.Transport.SetGhost(true);

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
        Assert.True(state.Transport.Previewing);
        // 2 s in: x = 4.
        Assert.Equal(4f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void TurningTheGhostOffMidPreviewShowsThePreviewAndHandsOffWhenItStops()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        state.Transport.SetGhost(false);

        // 2 s in, now on the game camera: x = 4.
        Assert.Equal(4f, state.Transport.EditingFrame(1f, flying: false).Shown!.Value.Position.X, 1e-3f);
        Assert.Null(state.Transport.GhostFrame);
        state.Stop();
        Assert.Equal(4f, state.Transport.EditingFrame(1f, flying: false).FlyFrom!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void TheGhostSitsAtTheScrubHeadAndOnlyShowsInEdit()
    {
        var state = EditingThreePoints();
        state.Transport.ScrubTo(2.5);
        Assert.Null(state.Transport.GhostFrame);

        state.Transport.SetGhost(true);
        // 2.5 s: x = 5.
        Assert.Equal(5f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);

        state.Release();
        Assert.Null(state.Transport.GhostFrame);
    }

    [Fact]
    public void TheGhostNeverShowsAnEarlierPreviewsFrame()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);
        state.Play();
        state.Transport.EditingFrame(3f, flying: false);
        state.Stop();
        state.Transport.ScrubTo(1.0);

        state.Play();

        // Before the new preview's first frame the ghost is at the scrub head, 1 s: x = 2, not the old 3 s frame's x = 6.
        Assert.Equal(2f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void TheGhostsLookAheadIsTheSpotAheadOfTheScrubHead()
    {
        var state = EditingThreePoints();
        state.ChangeTrack(t => t with { Aim = AimMode.PathTangent });
        state.Transport.ScrubTo(2.0);
        Assert.Null(state.Transport.GhostLookAhead);

        state.Transport.SetGhost(true);

        // At 2 yalms a second the scrub head at 2 s is at x = 4, and the default look ahead is 2.5 yalms: x = 6.5.
        Assert.Equal(6.5f, state.Transport.GhostLookAhead!.Value.X, 1e-3f);
    }

    [Fact]
    public void ReleasingTheScrubBarInEditGivesTheFreeCamTheScrubHeadsFrameUnlessTheGhostIsOn()
    {
        var state = EditingThreePoints();
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(3.0);

        // 3 s: x = 6.
        Assert.Equal(6f, state.Transport.EndScrub()!.Value.Position.X, 1e-3f);

        state.Transport.SetGhost(true);
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(4.0);
        Assert.Null(state.Transport.EndScrub());
    }

    [Fact]
    public void ReleasingTheScrubBarLiveGivesTheFreeCamNothing()
    {
        var state = LiveTwoPoints();
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(1.0);

        Assert.Null(state.Transport.EndScrub());
    }

    [Fact]
    public void AScrubReleaseDropsAWaitingHandOff()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);
        state.Transport.BeginScrub();
        state.Transport.ScrubTo(3.0);
        state.Transport.EndScrub();

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void JumpingToAPointMovesTheScrubHeadAndTheGhostThereAndGivesTheFreeCamItsFrame()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);

        // Point 1 is at x = 10, reached at 5 s.
        Assert.Equal(10f, state.JumpToPoint(1)!.Value.Position.X, 1e-3f);
        Assert.Equal(5.0, state.Transport.ScrubHead, 5);
        Assert.Equal(10f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
        Assert.Null(state.JumpToPoint(3));
    }

    [Fact]
    public void JumpingToAPointOutsideEditDoesNothing() => Assert.Null(LiveTwoPoints().JumpToPoint(0));

    [Fact]
    public void JumpingToAPointDropsAWaitingHandOff()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        state.JumpToPoint(1);

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeavingEditDropsAWaitingHandOff(bool toLive)
    {
        var state = EditingThreePoints();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        if (toLive)
            state.Cue();
        else
            state.Release();
        state.Edit();

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void NewPointsComeFromAPreviewOnTheGameCameraAndFromTheCameraOtherwise()
    {
        var state = EditingThreePoints();
        Assert.Null(state.Transport.FrameForNewPoints);
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);

        // 1 s in: x = 2.
        Assert.Equal(2f, state.Transport.FrameForNewPoints!.Value.Position.X, 1e-3f);
        state.Transport.SetGhost(true);
        Assert.Null(state.Transport.FrameForNewPoints);
    }

    [Fact]
    public void TurningTheGhostOffWhileNothingPlaysHandsNothingOff()
    {
        var state = EditingThreePoints();
        state.Transport.SetGhost(true);
        state.Transport.EditingFrame(1f, flying: false);

        state.Transport.SetGhost(false);

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void AHandOffBeforeAPreviewShowsAFrameStartsFromTheScrubHead()
    {
        var state = EditingThreePoints();
        state.Play();
        state.Transport.EditingFrame(1f, flying: false);
        state.Stop();
        state.Transport.ScrubTo(4.0);

        // A second preview starts and stops before any editing frame shows it.
        state.Play();
        state.Stop();

        // The free-cam starts from the scrub head, 4 s: x = 8.
        Assert.Equal(8f, state.Transport.EditingFrame(1f, flying: false).FlyFrom!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void TheGhostShowsThePreviewsSmoothedFrame()
    {
        var (state, characters) = EditingWatchingGuard();
        state.Transport.SetGhost(true);
        state.Play();
        state.Transport.EditingFrame(1f / 60f, flying: false);
        GuardAt(characters, WatchedAtB);

        var played = state.Transport.AdvancePreview(1f / 60f)!.Value;

        // Heavy smoothing keeps the preview looking well short of Guard's new spot, where the scrub head's frame would snap.
        Assert.True(Vector3.Dot(played.Forward, Vector3.Normalize(WatchedAtB - played.Position)) < 0.999f);
        Assert.Equal(played, state.Transport.GhostFrame);
    }

    [Fact]
    public void AHandOffStartsFromExactlyTheFrameLastShown()
    {
        var (state, characters) = EditingWatchingGuard();
        state.Play();
        state.Transport.EditingFrame(1f / 60f, flying: false);
        GuardAt(characters, WatchedAtB);
        var shown = state.Transport.EditingFrame(1f / 60f, flying: false).Shown!.Value;
        state.Stop();

        // Heavy smoothing keeps the shown frame well short of Guard's new spot, where the scrub head's frame would snap.
        Assert.True(Vector3.Dot(shown.Forward, Vector3.Normalize(WatchedAtB - shown.Position)) < 0.999f);
        Assert.Equal(shown, state.Transport.EditingFrame(1f / 60f, flying: false).FlyFrom);
    }

    [Fact]
    public void TheRateIsRefusedInOff()
    {
        var state = new SessionState();

        Assert.Equal(RateOnlyInEdit, state.Transport.SetPlaybackRate(0.5f));
        Assert.Equal(1f, state.Transport.PlaybackRate);
    }

    // EditingThreePoints with slot 0 on its track (x = 2t over 10 s) and slot 1 on a 2 s track, x = 2t to x = 4; nothing cut to yet.
    private static SessionState EditingWithSlots()
    {
        var state = EditingThreePoints();
        var edited = state.EditedTrackId;
        state.AddTrack();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));
        state.AssignSlot(1, state.EditedTrackId);
        state.AssignSlot(0, edited);
        state.SwitchTrack(edited);
        return state;
    }

    // Cuts Edit's switchboard to slot.
    private static void CutTo(SessionState state, int slot)
    {
        state.Board!.Click(slot);
        state.Board.Cut();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASwitchboardPreviewShowsOnTheEditCameraAtNormalSpeed(bool ghost)
    {
        var state = EditingWithSlots();
        state.Transport.SetGhost(ghost);
        state.Transport.SetPlaybackRate(0.5f);
        CutTo(state, 0);

        var frame = state.Transport.EditingFrame(1f, flying: false);

        // 1 s at normal speed, not half: x = 2.
        Assert.Equal(2f, frame.Shown!.Value.Position.X, 1e-3f);
        Assert.Null(frame.FlyFrom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlyingStopsASwitchboardPreviewAndHandsTheFreeCamItsLastFrameOnce(bool ghost)
    {
        var state = EditingWithSlots();
        state.Transport.SetGhost(ghost);
        CutTo(state, 0);
        state.Transport.EditingFrame(1f, flying: false);

        var frame = state.Transport.EditingFrame(1f, flying: true);

        Assert.Null(state.Board!.Program);
        Assert.Null(frame.Shown);
        // The last frame shown was 1 s in: x = 2.
        Assert.Equal(2f, frame.FlyFrom!.Value.Position.X, 1e-3f);
        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void AStoppedSwitchboardPreviewHandsTheFreeCamItsLastFrame()
    {
        var state = EditingWithSlots();
        CutTo(state, 0);
        state.Transport.EditingFrame(3f, flying: false);
        state.RenameTrack(state.EditedTrackId, "Wide");

        var frame = state.Transport.EditingFrame(1f, flying: false);

        // Stopped 3 s in: x = 6.
        Assert.Null(frame.Shown);
        Assert.Equal(6f, frame.FlyFrom!.Value.Position.X, 1e-3f);
    }

    [Fact]
    public void ASwitchboardShotHoldsItsLastFrameAtItsEnd()
    {
        var state = EditingWithSlots();
        CutTo(state, 1);

        // Slot 1's track ends at 2 s on x = 4, and holds there.
        Assert.Equal(4f, state.Transport.EditingFrame(3f, flying: false).Shown!.Value.Position.X, 1e-3f);
        Assert.Equal(4f, state.Transport.EditingFrame(1f, flying: false).Shown!.Value.Position.X, 1e-3f);
        Assert.True(state.Board!.HasProgram);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEditedTracksPreviewReplacesASwitchboardPreview(bool ghost)
    {
        var state = EditingWithSlots();
        state.Transport.SetGhost(ghost);
        state.Transport.ScrubTo(4.0);
        CutTo(state, 0);
        state.Transport.EditingFrame(1f, flying: false);

        state.Play();
        var frame = state.Transport.EditingFrame(1f, flying: false);

        // The track preview plays from the 4 s scrub head, 1 s on: x = 10. The switchboard's last frame was 1 s in: x = 2.
        if (ghost)
        {
            Assert.Null(frame.Shown);
            Assert.Equal(2f, frame.FlyFrom!.Value.Position.X, 1e-3f);
            Assert.Equal(10f, state.Transport.GhostFrame!.Value.Position.X, 1e-3f);
        }
        else
        {
            Assert.Equal(10f, frame.Shown!.Value.Position.X, 1e-3f);
            Assert.Null(frame.FlyFrom);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewPointsComeFromASwitchboardPreviewOnTheCamera(bool ghost)
    {
        var state = EditingWithSlots();
        state.Transport.SetGhost(ghost);
        CutTo(state, 0);
        state.Transport.EditingFrame(1f, flying: false);

        // 1 s in: x = 2.
        Assert.Equal(2f, state.Transport.FrameForNewPoints!.Value.Position.X, 1e-3f);
        state.Transport.EditingFrame(1f, flying: true);
        Assert.Null(state.Transport.FrameForNewPoints);
    }

    [Fact]
    public void JumpingToAPointDropsAWaitingSwitchboardHandOff()
    {
        var state = EditingWithSlots();
        CutTo(state, 0);
        state.Transport.EditingFrame(1f, flying: false);

        state.JumpToPoint(1);

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }

    [Fact]
    public void AScrubReleaseDropsAWaitingSwitchboardHandOff()
    {
        var state = EditingWithSlots();
        CutTo(state, 0);
        state.Transport.EditingFrame(1f, flying: false);
        state.Transport.BeginScrub();
        state.Transport.EndScrub();

        Assert.Equal(default, state.Transport.EditingFrame(1f, flying: false));
    }
}
