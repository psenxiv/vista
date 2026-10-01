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

    // Editing a track with points at x = 0 and 10, a 2 s shot, which is the playlist's only entry.
    private static SessionState EditingAPlaylistOfTwoPoints()
    {
        var state = EditingTwoPoints();
        state.AddToPlaylist([state.EditedTrackId]);
        return state;
    }

    [Fact]
    public void EditFromLiveLeavesLive()
    {
        var state = LiveTwoPoints();
        Assert.Equal(EditOutcome.FromLive, state.Edit());
        Assert.Equal(CameraMode.Editing, state.Mode);
        Assert.Null(state.LivePlaylist);
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void PlayWithNoPointsInEditIsRefusedAndChangesNothing()
    {
        var editing = new SessionState();
        editing.Edit();
        Assert.Equal(PlayOutcome.Refused, editing.Play());
        Assert.Equal(CameraMode.Editing, editing.Mode);
        Assert.False(editing.IsPlaying);
    }

    [Theory]
    [InlineData(CameraMode.Off, PlayOutcome.CuedFromGame)]
    [InlineData(CameraMode.View, PlayOutcome.CuedFromGame)]
    [InlineData(CameraMode.Editing, PlayOutcome.Cued)]
    public void CueEntersLivePausedOnThePlaylistsFirstFrame(CameraMode from, PlayOutcome outcome)
    {
        var state = EditingAPlaylistOfTwoPoints();
        if (from != CameraMode.Editing)
            state.Release(from);

        // The game camera was at x = 1 (WellFormedFrame); the playlist's first frame is the track's first point, x = 0.
        Assert.Equal(outcome, state.Cue(WellFormedFrame));

        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.LocksInput);
        Assert.False(state.IsPlaying);
        // Paused, so a second later the camera is still on the first frame and the head at 0.
        Assert.Equal(0f, state.LiveFrame(1f)!.Value.Position.X, 1e-4f);
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Fact]
    public void CueInLiveIsRefusedAndLeavesTheShotPlaying()
    {
        var state = LiveTwoPoints();
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.True(state.IsPlaying);
    }

    [Theory]
    [InlineData(CameraMode.Off)]
    [InlineData(CameraMode.View)]
    public void PlayFromTheGameEntersLiveAndPlaysThePlaylistFromItsStart(CameraMode from)
    {
        var state = EditingAPlaylistOfTwoPoints();
        state.Release(from);

        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());

        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.True(state.IsPlaying);
        // Playing from 0, so a second later the head is at 1 s.
        state.LiveFrame(1f);
        Assert.Equal(1.0, Head(state), 1e-5);
    }

    [Fact]
    public void LiveIsRefusedWhileThePlaylistHasNothingToPlay()
    {
        // The playlist's only entry is a track with no points.
        var state = new SessionState();
        state.Edit();
        state.AddToPlaylist([state.EditedTrackId]);

        Assert.False(state.CanGoLive);
        Assert.Equal("Add a track with points to the playlist.", state.LiveRefusal);
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.Equal(CameraMode.Editing, state.Mode);

        state.Release();
        Assert.False(state.CanStart);
        Assert.Equal(PlayOutcome.Refused, state.Cue());
        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(CameraMode.Off, state.Mode);

        state.ReportFault("draw");
        Assert.Equal(
            "Vista has stopped. Reload it in /xlplugins, or check for an update if that doesn't help.",
            state.LiveRefusal
        );
    }

    [Fact]
    public void LiveIsntRefusedOnceThePlaylistHasATrackWithPoints()
    {
        var state = EditingAPlaylistOfTwoPoints();

        Assert.True(state.CanGoLive);
        Assert.Null(state.LiveRefusal);
    }

    [Fact]
    public void PlayWhilePlayingOnlyReHides()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(1f);
        Assert.Equal(PlayOutcome.ReHid, state.Play());
        Assert.True(state.IsPlaying);
        Assert.Equal(1.0, Head(state), 1e-5);
    }

    [Fact]
    public void PlayWhilePausedResumes()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(1f);
        state.Stop();
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.True(state.IsPlaying);
        Assert.Equal(1.0, Head(state), 1e-5);
    }

    [Fact]
    public void PlayWhenFinishedStartsAgainFromZero()
    {
        // The shot is 2 s long, so 6 s holds it at its end, 2 s.
        var state = LiveTwoPoints();
        state.LiveFrame(6f);
        Assert.False(state.IsPlaying);
        Assert.Equal(2.0, Head(state), 1e-5);

        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.True(state.IsPlaying);
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Fact]
    public void PlayWhenPausedAndFinishedStartsAgainFromZero()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(6f);
        state.Stop();
        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.True(state.IsPlaying);
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Theory]
    [InlineData("playing")]
    [InlineData("paused")]
    [InlineData("finished")]
    public void RestartWhileLivePlaysFromZero(string was)
    {
        // 1 s into the 2 s shot, or 6 s on, which holds it at its end.
        var state = LiveTwoPoints();
        state.LiveFrame(was == "finished" ? 6f : 1f);
        if (was == "paused")
            state.Stop();

        Assert.Equal(PlayOutcome.Started, state.Restart());

        Assert.True(state.IsPlaying);
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Theory]
    [InlineData(CameraMode.Off)]
    [InlineData(CameraMode.View)]
    public void RestartIsRefusedWhileTheGameHasTheCamera(CameraMode mode)
    {
        var state = LiveTwoPoints();
        state.Release(mode);
        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.Equal(mode, state.Mode);
    }

    [Fact]
    public void StopPausesOnlyWhileLive()
    {
        Assert.False(new SessionState().Stop());
        Assert.False(EditingTwoPoints().Stop());

        var live = LiveTwoPoints();
        live.LiveFrame(1f);
        Assert.True(live.Stop());
        Assert.False(live.IsPlaying);
        Assert.Equal(CameraMode.Live, live.Mode);
        // Paused at 1 s, so another second leaves the head there.
        live.LiveFrame(1f);
        Assert.Equal(1.0, Head(live), 1e-5);
    }

    [Fact]
    public void EveryVisitToLiveStartsThePlaylistFromItsBeginning()
    {
        var state = LiveTwoPoints();
        state.LiveFrame(1f);
        Assert.Equal(1.0, Head(state), 1e-5);

        state.Edit();
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(0.0, Head(state), 1e-9);

        state.Play();
        state.LiveFrame(1f);
        state.Release();
        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    public static TheoryData<string, Action<SessionState>> WaysOutOfLive =>
        new()
        {
            { "edit", s => s.Edit() },
            { "off", s => s.Release() },
            { "view", s => s.Release(CameraMode.View) },
            { "fault", s => s.ReportFault("draw") },
        };

    [Theory]
    [MemberData(nameof(WaysOutOfLive))]
    public void AVisitToLiveLeavesTheSwitchboardsPlaceAlone(string _, Action<SessionState> leave)
    {
        // OnAirScene saves slot 0 on Program at 3 s, slot 1 Next, and slots 1 and 2 resuming at 2 s and 4 s; Track 1 gets a 2 s shot and goes in the playlist.
        var scene = OnAirScene();
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        var state = new SessionState();
        state.LoadScene(scene);
        var saved = state.Scene.Switchboard.Live;

        Assert.Equal(PlayOutcome.StartedFromGame, state.Play());
        state.LiveFrame(0.5f);
        state.Edit();
        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(PlayOutcome.Resumed, state.Play());
        state.LiveFrame(0.5f);
        state.LivePlaylist!.BeginScrub();
        state.LivePlaylist.ScrubTo(1.5);
        state.LivePlaylist.EndScrub();
        state.Stop();
        state.Restart();
        state.LiveFrame(0.5f);
        leave(state);

        Assert.NotEqual(CameraMode.Live, state.Mode);
        Assert.Same(saved, state.Scene.Switchboard.Live);
        SameAir(new OnAir(0, 1, 3.0, Resume((1, 2.0), (2, 4.0))), state.Scene.Switchboard.Live);
    }

    [Fact]
    public void ThePlaylistShowsAsPlayingOnlyInLive()
    {
        var state = EditingAPlaylistOfTwoPoints();
        Assert.Null(state.LivePlaylist);
        Assert.Null(state.PlayingEntry);

        state.Release();
        Assert.Null(state.LivePlaylist);
        Assert.Null(state.PlayingEntry);

        state.Cue();
        Assert.NotNull(state.LivePlaylist);
        Assert.Equal(Entries(state.Scene)[0], state.PlayingEntry);
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
        Assert.Null(live.LivePlaylist);
        Assert.False(live.IsPlaying);
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

    // IsPlaying is true while an Edit preview runs, or Live's shot runs unpaused and unfinished. Every expectation below
    // is read off the mode and the playing state the case sets up, not off the predicate itself.

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

        Assert.True(state.Stop());
        Assert.False(state.IsPlaying);

        Assert.Equal(PlayOutcome.Resumed, state.Play());
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void IsPlayingIsFalseOnceALiveShotFinishes()
    {
        var state = LiveTwoPoints();

        // EditingTwoPoints is a single 2 s leg, so the cycle ends at 2 s.
        state.LiveFrame(6f);

        Assert.Equal(2.0, Head(state), 1e-5);
        Assert.False(state.IsPlaying);
    }

    // CanStart is the edited track's points in Edit, a playlist that can play in Off, View and Live; CanRestart adds that Off and View can't.

    [Fact]
    public void CanStartNeedsPointsInEditAndAPlaylistThatCanPlayOutsideIt()
    {
        // A new scene's playlist is empty, and its track has no points.
        var state = new SessionState();
        Assert.False(state.CanStart);

        state.Edit();
        Assert.False(state.CanStart);

        state.ChangeTrack(WithTwoPoints);
        Assert.True(state.CanStart);

        // The track has points, but the playlist is still empty.
        state.Release();
        Assert.False(state.CanStart);

        state.Edit();
        state.AddToPlaylist([state.EditedTrackId]);
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
        // Live plays the edited track, yet its scrub head stays at 1.5 s while Live plays and after.
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
