using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionStateTests
{
    [Fact]
    public void StartsInOffWithAnEmptyTrackAndNothingLocked()
    {
        var state = new SessionState();
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.Empty(state.Track.Points);
        Assert.False(state.LocksInput);
    }

    [Fact]
    public void EditFromViewEntersEditingAndLocksInput()
    {
        var state = new SessionState();
        Assert.Equal(EditOutcome.FromGame, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void EditWhileEditingChangesNothing()
    {
        var state = EditingTwoPoints();
        Assert.Equal(EditOutcome.Unchanged, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void EditFromLiveLeavesLive()
    {
        var state = LiveTwoPoints();
        Assert.Equal(EditOutcome.FromLive, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.Null(state.Board);
    }

    [Fact]
    public void PlayWithNoPointsInEditIsRefusedAndChangesNothing()
    {
        var editing = new SessionState();
        editing.Edit();
        Assert.Equal(PlayOutcome.Refused, editing.Play());
        Assert.Equal(CameraMode.Editing, editing.Mode);
        Assert.Null(editing.Board);
    }

    [Fact]
    public void CueFromEditingGoesLive()
    {
        var state = EditingTwoPoints();
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.NotNull(state.Board);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void CueInLiveIsRefusedAndLeavesTheShotPlaying()
    {
        var state = LiveTwoPoints();
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void PlayFromViewSaysItStartedFromGame()
    {
        var state = LiveTwoPoints();
        state.Release();
        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void PlayWhilePlayingOnlyReHides()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.Equal(1.0, state.Board!.Head, 5);
    }

    [Fact]
    public void PlayWhilePausedResumes()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(1f);
        state.Stop();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.False(state.Board!.IsPaused);
        Assert.Equal(1.0, state.Board.Head, 5);
    }

    [Fact]
    public void PlayWhenFinishedStartsAgainFromZero()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(6f);
        Assert.True(state.Board!.IsFinished);
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.Board.Head);
    }

    [Fact]
    public void PlayWhenPausedAndFinishedStartsAgainFromZero()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(6f);
        state.Stop();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.False(state.Board!.IsPaused);
        Assert.Equal(0.0, state.Board.Head);
    }

    [Fact]
    public void CueFromViewSaysItCuedFromGame()
    {
        var state = EditingTwoPoints();
        state.Release();
        Assert.Equal(PlayOutcome.CuedFromGame, state.Cue());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void RestartWhileLiveStartsFromZero()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(2f);
        Assert.Equal(PlayOutcome.Started, state.Restart());
        Assert.Equal(0.0, state.Board!.Head);
    }

    [Fact]
    public void RestartIsRefusedWhileTheGameHasTheCamera()
    {
        var state = LiveTwoPoints();
        state.Release(CameraMode.View);
        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.Equal(CameraMode.View, state.Mode);
    }

    [Fact]
    public void StopPausesOnlyWhileLive()
    {
        Assert.False(new SessionState().Stop());
        Assert.False(EditingTwoPoints().Stop());

        var live = LiveTwoPoints();
        Assert.True(live.Stop());
        Assert.True(live.Board!.IsPaused);
        Assert.Equal(CameraMode.Live, live.Mode);
    }

    [Fact]
    public void ReleaseReturnsEverythingToOff()
    {
        Assert.False(new SessionState().Release());

        var editing = EditingTwoPoints();
        Assert.True(editing.Release());
        Assert.Equal(CameraMode.Off, editing.Mode);

        var live = LiveTwoPoints();
        Assert.True(live.Release());
        Assert.Equal(CameraMode.Off, live.Mode);
        Assert.Null(live.Board);
        Assert.Null(live.LiveFrame(1f / 60f));
    }

    [Fact]
    public void ReleaseKeepsTheTrack()
    {
        var state = EditingTwoPoints();
        state.Release();
        Assert.Equal(2, state.Track.Points.Count);
    }

    [Fact]
    public void TheTrackChangesOnlyWhileEditing()
    {
        const string refused = "The track can only change while editing.";

        var view = new SessionState();
        Assert.Equal(refused, view.ChangeTrack(t => TrackEditing.Append(t, Point(0f))));
        Assert.Empty(view.Track.Points);

        var live = LiveTwoPoints();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
        Assert.Equal(2, live.Track.Points.Count);

        live.Stop();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
    }

    [Fact]
    public void ChangeTrackAppliesWhileEditing()
    {
        var state = EditingTwoPoints();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 1, 8f)));
        Assert.Equal(8f, state.World.Evaluator.LegSeconds(1), 3);
    }

    [Fact]
    public void ChangeTrackReturnsTheRefusalAndKeepsTheTrack()
    {
        var state = EditingTwoPoints();
        var before = state.Track;

        // Two points make one leg, leg 1.
        Assert.Equal(
            "Leg index must be 1..1 for a 2-point track.",
            state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 2, 1f))
        );
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ChangeTrackRefusesATrackThatCannotBePlayed()
    {
        var state = EditingTwoPoints();
        var before = state.Track;
        Assert.Contains("timing entry", state.ChangeTrack(t => t with { Timing = [] }));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ReleasingToViewAndBackToOffKeepsInputUnlocked()
    {
        var editing = EditingTwoPoints();
        Assert.True(editing.Release(CameraMode.View));
        Assert.Equal(CameraMode.View, editing.Mode);
        Assert.False(editing.LocksInput);
        Assert.True(editing.Released);

        Assert.False(editing.Release());
        Assert.Equal(CameraMode.Off, editing.Mode);
        Assert.False(editing.LocksInput);
        Assert.Throws<ArgumentOutOfRangeException>(() => editing.Release(CameraMode.Live));
    }

    // Two tracks; the first has points at x = 0, 10, 20 at 2 yalms per second, a 10 s track.
    private static Scene Loaded() => SceneEditing.Add(OnePlaylist([Build3PointTrack()])).Scene;

    [Fact]
    public void LoadingASceneEditsItsFirstTrackAndStartsAfresh()
    {
        var state = EditingTwoPoints();
        state.AddTrack();
        state.AddToEnd(Point(5f));
        state.Selection.Select(0);
        state.Transport.ScrubTo(1.0);
        var loaded = Loaded();

        Assert.Null(state.LoadScene(loaded));

        Assert.Same(loaded, state.Scene);
        Assert.Equal(loaded.Tracks[0].Id, state.EditedTrackId);
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.Null(state.Selection.Point);
        Assert.Null(state.Selection.Key);
        Assert.Equal(0.0, state.Transport.ScrubHead);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void LoadingASceneLeavesOffInOff()
    {
        var state = new SessionState();
        var loaded = Loaded();

        Assert.Null(state.LoadScene(loaded));

        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.Same(loaded, state.Scene);
    }

    [Fact]
    public void LoadingASceneIsRefusedWhileLive()
    {
        var state = LiveTwoPoints();
        var before = state.Scene;

        Assert.Equal("A scene can't be loaded while Live.", state.LoadScene(Loaded()));
        Assert.Same(before, state.Scene);
    }

    [Fact]
    public void LoadingASceneStopsAPreview()
    {
        var state = EditingTwoPoints();
        state.Play();

        state.LoadScene(Loaded());

        Assert.False(state.Transport.Previewing);
    }

    [Fact]
    public void LoadingASceneDropsALiveEditWithoutRecordingIt()
    {
        var state = EditingTwoPoints();
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(20f));
        var loaded = Loaded();

        state.LoadScene(loaded);
        state.EndLiveEdit();

        Assert.Same(loaded, state.Scene);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void LoadingASceneShowsItsFirstTrackIfHidden()
    {
        var scene = Loaded();
        var first = scene.Tracks[0].Id;
        var second = scene.Tracks[1].Id;

        var state = new SessionState();
        state.LoadScene(SceneEditing.SetHidden(SceneEditing.SetHidden(scene, [first], true), [second], true));

        Assert.Equal(new HashSet<Guid> { second }, state.Scene.Hidden);
        Assert.Same(scene.Tracks, state.Scene.Tracks);
    }

    // IsPlaying is Previewing || (Live && !IsPaused && !IsFinished). Every expectation below is
    // read off the mode and director state the case sets up, not off the predicate itself.

    [Fact]
    public void IsPlayingIsFalseWhileReleased()
    {
        var state = new SessionState();
        Assert.Equal(CameraMode.Off, state.Mode);
        Assert.False(state.IsPlaying);

        state.Edit();
        state.Release(CameraMode.View);
        Assert.Equal(CameraMode.View, state.Mode);
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingFollowsAnEditPreview()
    {
        var state = EditingTwoPoints();
        Assert.False(state.IsPlaying);

        Assert.Equal(PlayOutcome.Previewed, state.Play());
        Assert.True(state.IsPlaying);

        state.Stop();
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingFollowsALiveShot()
    {
        var state = LiveTwoPoints();
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.IsPlaying);

        state.Stop();
        Assert.True(state.Board!.IsPaused);
        Assert.False(state.IsPlaying);

        state.Play();
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingIsFalseOnceALiveShotFinishes()
    {
        var state = LiveTwoPoints();

        // EditingTwoPoints is a single 2 s leg, so the cycle ends at 2 s.
        state.LiveFrame(6f);

        Assert.True(state.Board!.IsFinished);
        Assert.False(state.IsPlaying);
    }

    // CanStart is the edited track's points in Edit, a Program shot in Live, else CanGoLive; CanRestart adds that Off and View can't.

    [Fact]
    public void CanStartNeedsPointsInEditAndAProgramShotInLive()
    {
        var state = new SessionState();
        Assert.True(state.CanStart);

        state.Edit();
        Assert.False(state.CanStart);

        state.ChangeTrack(WithTwoPoints);
        Assert.True(state.CanStart);

        state.Release();
        Assert.True(state.CanStart);

        Assert.True(LiveTwoPoints().CanStart);
    }

    [Fact]
    public void CanRestartIsFalseWhileTheGameHasTheCamera()
    {
        var state = EditingTwoPoints();
        Assert.True(state.CanRestart);

        state.Release();
        Assert.False(state.CanRestart);

        state.Release(CameraMode.View);
        Assert.False(state.CanRestart);

        Assert.True(LiveTwoPoints().CanRestart);
    }

    [Fact]
    public void TheOverlayShowsInViewAndInEditWithoutAPreview()
    {
        var state = new SessionState();
        Assert.False(state.OverlayShown);
        Assert.False(state.OverlayEditable);

        state.Release(CameraMode.View);
        Assert.True(state.OverlayShown);
        Assert.False(state.OverlayEditable);

        state.Edit();
        state.ChangeTrack(WithTwoPoints);
        Assert.True(state.OverlayShown);
        Assert.True(state.OverlayEditable);

        state.Play();
        Assert.False(state.OverlayShown);
        Assert.False(state.OverlayEditable);

        var live = LiveTwoPoints();
        Assert.False(live.OverlayShown);
        Assert.False(live.OverlayEditable);
    }

    [Fact]
    public void TheOverlayStaysShownAndEditableWhileAPreviewPlaysOnTheGhost()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(WithTwoPoints);
        state.Transport.SetGhost(true);

        state.Play();

        Assert.True(state.OverlayShown);
        Assert.True(state.OverlayEditable);
    }

    [Fact]
    public void LiveLeavesTheScrubHeadWhereItWas()
    {
        // The edited track is Live's Program shot, yet its scrub head stays at 1.5 s while Live plays and after.
        var state = EditingTwoPoints();
        state.Transport.ScrubTo(1.5);
        state.AddToPlaylist([state.EditedTrackId]);
        GoLive(state);
        state.LiveFrame(0.25f);
        Assert.Equal(1.5, state.Transport.ScrubHead, 9);

        state.Edit();

        Assert.Equal(1.5, state.Transport.ScrubHead, 9);
    }

    [Fact]
    public void PointsAreAddedOnlyWhileEditingAndNotScrubbing()
    {
        var state = new SessionState();
        Assert.Equal("Points can only be added while editing.", state.AddPointRefusal);

        state.Edit();
        Assert.Null(state.AddPointRefusal);

        state.Transport.BeginScrub();
        Assert.Equal("Points cannot be added while scrubbing.", state.AddPointRefusal);
    }
}
