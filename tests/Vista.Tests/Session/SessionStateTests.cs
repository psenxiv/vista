using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Session;

public class SessionStateTests
{
    // Two points, one 5 s leg.
    private static SessionState EditingWithTrack()
    {
        var state = new SessionState();
        state.Edit();
        state.ChangeTrack(t => TrackEditing.Append(TrackEditing.Append(t, Point(0f)), Point(10f)));
        return state;
    }

    private static SessionState Live()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Cue();
        state.Play();
        return state;
    }

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
        var state = EditingWithTrack();
        Assert.Equal(EditOutcome.Unchanged, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void EditFromLiveTakesTheDirectorOffline()
    {
        var state = Live();
        Assert.Equal(EditOutcome.FromLive, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.False(state.Director.IsLive);
    }

    [Fact]
    public void PlayWithNoPointsIsRefusedAndChangesNothing()
    {
        var view = new SessionState();
        Assert.Equal(PlayOutcome.Refused, view.Play());
        Assert.Equal(CameraMode.Off, view.Mode);

        var editing = new SessionState();
        editing.Edit();
        Assert.Equal(PlayOutcome.Refused, editing.Play());
        Assert.Equal(CameraMode.Editing, editing.Mode);
        Assert.False(editing.Director.IsLive);
    }

    [Fact]
    public void CueThenPlayFromEditingGoesLive()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.Director.IsLive);
        Assert.True(state.LocksInput);
    }

    [Fact]
    public void PlayFromViewSaysItStartedFromGame()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Release();
        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void PlayWhilePlayingOnlyReHides()
    {
        var state = Live();
        state.Director.Tick(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.Equal(1.0, state.Director.ShotTime, 5);
    }

    [Fact]
    public void PlayWhilePausedResumes()
    {
        var state = Live();
        state.Director.Tick(1f);
        state.Stop();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(1.0, state.Director.ShotTime, 5);
    }

    [Fact]
    public void PlayWhenFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        Assert.True(state.Director.IsFinished);
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.Director.ShotTime);
    }

    [Fact]
    public void PlayWhenPausedAndFinishedStartsAgainFromZero()
    {
        var state = Live();
        state.Director.Tick(6f);
        state.Stop();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.False(state.Director.IsPaused);
        Assert.Equal(0.0, state.Director.ShotTime);
    }

    [Fact]
    public void CueFromEditingGoesLivePausedAtTheStart()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.Director.IsPaused);
        state.Director.Tick(1f);
        Assert.Equal(0.0, state.Director.ShotTime);
    }

    [Fact]
    public void PlayAfterCueStartsTheShot()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Cue();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        state.Director.Tick(1f);
        Assert.Equal(1.0, state.Director.ShotTime, 5);
    }

    [Fact]
    public void CueFromViewSaysItCuedFromGame()
    {
        var state = EditingWithTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.Release();
        Assert.Equal(PlayOutcome.CuedFromGame, state.Cue());
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    [Fact]
    public void CueWithNoPointsIsRefused()
    {
        var state = new SessionState();
        state.Edit();
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.Equal(CameraMode.Editing, state.Mode);
    }

    [Fact]
    public void RestartWhileLiveStartsFromZero()
    {
        var state = Live();
        state.Director.Tick(2f);
        Assert.Equal(PlayOutcome.Started, state.Restart());
        Assert.Equal(0.0, state.Director.ShotTime);
    }

    [Fact]
    public void StopPausesOnlyWhileLive()
    {
        Assert.False(new SessionState().Stop());
        Assert.False(EditingWithTrack().Stop());

        var live = Live();
        Assert.True(live.Stop());
        Assert.True(live.Director.IsPaused);
        Assert.Equal(CameraMode.Live, live.Mode);
    }

    [Fact]
    public void ReleaseReturnsEverythingToOff()
    {
        Assert.False(new SessionState().Release());

        var editing = EditingWithTrack();
        Assert.True(editing.Release());
        Assert.Equal(CameraMode.Off, editing.Mode);

        var live = Live();
        Assert.True(live.Release());
        Assert.Equal(CameraMode.Off, live.Mode);
        Assert.False(live.Director.IsLive);
        Assert.Null(live.Director.Tick(1f / 60f));
    }

    [Fact]
    public void ReleaseKeepsTheTrack()
    {
        var state = EditingWithTrack();
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

        var live = Live();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
        Assert.Equal(2, live.Track.Points.Count);

        live.Stop();
        Assert.Equal(refused, live.ChangeTrack(t => TrackEditing.Append(t, Point(20f))));
    }

    [Fact]
    public void ChangeTrackAppliesWhileEditing()
    {
        var state = EditingWithTrack();
        Assert.Null(state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 1, 8f)));
        Assert.Equal(8f, state.World.Evaluator.LegSeconds(1), 3);
    }

    [Fact]
    public void ChangeTrackReturnsTheRefusalAndKeepsTheTrack()
    {
        var state = EditingWithTrack();
        var before = state.Track;

        Assert.Contains("leg index", state.ChangeTrack(t => TrackEditing.SetLegDuration(t, 2, 1f)));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ChangeTrackRefusesATrackThatCannotBePlayed()
    {
        var state = EditingWithTrack();
        var before = state.Track;
        Assert.Contains("timing entry", state.ChangeTrack(t => t with { Timing = [] }));
        Assert.Same(before, state.Track);
    }

    [Fact]
    public void ReleasingToViewAndBackToOffKeepsInputUnlocked()
    {
        var editing = EditingWithTrack();
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
    private static Scene Loaded() => SceneEditing.Add(new Scene([Build3PointTrack()], new HashSet<Guid>(), [])).Scene;

    [Fact]
    public void LoadingASceneEditsItsFirstTrackAndStartsAfresh()
    {
        var state = EditingWithTrack();
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
        var state = Live();
        var before = state.Scene;

        Assert.Equal("A scene can't be loaded while Live.", state.LoadScene(Loaded()));
        Assert.Same(before, state.Scene);
    }

    [Fact]
    public void LoadingASceneStopsAPreview()
    {
        var state = EditingWithTrack();
        state.Play();

        state.LoadScene(Loaded());

        Assert.False(state.Transport.Previewing);
    }

    [Fact]
    public void LoadingASceneDropsALiveEditWithoutRecordingIt()
    {
        var state = EditingWithTrack();
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
        var state = EditingWithTrack();
        Assert.False(state.IsPlaying);

        Assert.Equal(PlayOutcome.Previewed, state.Play());
        Assert.True(state.IsPlaying);

        state.Stop();
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingFollowsALiveShot()
    {
        var state = Live();
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.IsPlaying);

        state.Stop();
        Assert.True(state.Director.IsPaused);
        Assert.False(state.IsPlaying);

        state.Play();
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingIsFalseOnceALiveShotFinishes()
    {
        var state = Live();

        // EditingWithTrack is a single 5 s leg, so the cycle ends at 5 s.
        state.Director.Tick(6f);

        Assert.True(state.Director.IsFinished);
        Assert.False(state.IsPlaying);
    }
}
