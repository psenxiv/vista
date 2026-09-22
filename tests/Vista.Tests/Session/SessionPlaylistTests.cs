using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Session;

public class SessionPlaylistTests
{
    private static ControlPoint Point(float x) => new(new Vector3(x, 0f, 0f), 0f, 0f, 1f);

    // Editing; Track 1 has points at x = 0, 10, 20 (a 10 s shot at 2 yalms per second); Track 2 has points at x = 0, 4 (2 s).
    private static SessionState Editing()
    {
        var state = new SessionState();
        state.Edit();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToEnd(Point(20f));
        state.AddTrack();
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));
        return state;
    }

    private static Guid First(SessionState state) => state.Scene.Tracks[0].Id;

    private static Guid Second(SessionState state) => state.Scene.Tracks[1].Id;

    [Fact]
    public void PlaylistEditsAreUndoSteps()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist(First(state)));
        Assert.Null(state.AddToPlaylist(Second(state), 0));
        var entry = state.Scene.Playlist[1].Id;
        Assert.Null(state.SetEntryLoops(entry, 2));
        Assert.Null(state.MovePlaylistEntry(1, 0));
        Assert.Null(state.RemoveFromPlaylist(entry));

        Assert.Single(state.Scene.Playlist);
        state.Undo();
        state.Undo();
        Assert.Equal(2, state.Scene.Playlist[1].Loops);
        state.Undo();
        Assert.Null(state.Scene.Playlist[1].Loops);
    }

    [Fact]
    public void PlaylistEditsAreRefusedUnlessEditing()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        state.Cue();

        Assert.NotNull(state.AddToPlaylist(First(state)));
        Assert.NotNull(state.RemoveFromPlaylist(state.Scene.Playlist[0].Id));
        Assert.NotNull(state.SetEntryLoops(state.Scene.Playlist[0].Id, 3));
        Assert.Single(state.Scene.Playlist);
    }

    [Fact]
    public void MovePlaylistEntryIsRefusedWhileLive()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        state.AddToPlaylist(Second(state));
        state.Cue();

        Assert.NotNull(state.MovePlaylistEntry(1, 0));
        Assert.Equal(First(state), state.Scene.Playlist[0].TrackId);
        Assert.Equal(Second(state), state.Scene.Playlist[1].TrackId);
    }

    [Fact]
    public void DeletingATrackRemovesItsEntriesInOneStep()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));

        state.DeleteTrack(Second(state));
        Assert.Single(state.Scene.Playlist);

        state.Undo();
        Assert.Equal(2, state.Scene.Playlist.Count);
    }

    [Fact]
    public void LiveIsRefusedWhenNothingCanPlay()
    {
        var state = Editing();
        Assert.False(state.CanGoLive);
        Assert.Equal(PlayOutcome.Refused, state.Cue());

        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        Assert.False(state.CanGoLive);

        state.AddToPlaylist(First(state));
        Assert.True(state.CanGoLive);
    }

    [Fact]
    public void PlayAndRestartFromOffAreRefusedWhenNothingCanPlay()
    {
        var state = Editing();
        state.Release();

        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(CameraMode.Off, state.Mode);

        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.Equal(CameraMode.Off, state.Mode);
    }

    [Fact]
    public void LiveCuesThePlaylistAtItsFirstPlayableEntryAndPlaysItInTurn()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));

        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(2.0, state.ScrubLength, 4);

        state.Play();
        state.Director.Tick(3f);
        Assert.Equal(state.Scene.Playlist[2].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.ScrubHead, 4);
        Assert.Equal(10.0, state.ScrubLength, 4);
    }

    [Fact]
    public void ScrubbingLiveSeeksWithinThePlayingEntry()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(3f);

        state.BeginScrub();
        state.ScrubTo(7.0);
        state.EndScrub();

        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(7.0, state.ScrubHead, 4);
    }

    [Fact]
    public void ScrubbingPastThePlayingEntrysLengthClampsAndStaysOnIt()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();

        state.BeginScrub();
        state.ScrubTo(99.0);
        state.EndScrub();

        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(2.0, state.ScrubHead, 4);
    }

    [Fact]
    public void TheEndHoldsAndPlayStartsAgain()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.Cue();
        state.Play();
        state.Director.Tick(5f);

        Assert.True(state.Director.IsFinished);
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.Equal(2.0, state.ScrubHead, 4);

        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void RestartInLiveGoesBackToTheFirstEntry()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(5f);

        state.Restart();

        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void RestartInLiveSkipsALeadingEntryWithNoPoints()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(3f);

        state.Restart();

        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void EditFromLiveTakesTheShotTimeOnlyWhenTheEditedTrackIsPlaying()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(1f);

        state.Edit();
        Assert.Equal(1.0, state.ScrubHead, 4);

        state.SwitchTrack(First(state));
        state.Cue();
        state.Play();
        state.Director.Tick(1f);
        state.Edit();
        Assert.Equal(0.0, state.ScrubHead, 4);
    }

    [Fact]
    public void PlayingEntryIsNullUnlessLive()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        Assert.Null(state.PlayingEntry);
    }

    [Fact]
    public void SettingThePlaylistLoopIsOneUndoStep()
    {
        var state = Editing();

        Assert.Null(state.SetPlaylistLoops(true));
        Assert.True(state.Scene.PlaylistLoops);

        state.Undo();
        Assert.False(state.Scene.PlaylistLoops);
    }

    [Fact]
    public void SettingThePlaylistLoopIsRefusedUnlessEditing()
    {
        var state = Editing();
        state.AddToPlaylist(First(state));
        state.Cue();

        Assert.NotNull(state.SetPlaylistLoops(true));
        Assert.False(state.Scene.PlaylistLoops);
    }

    [Fact]
    public void LivePlaysALoopingPlaylistRoundAgain()
    {
        var state = Editing();
        state.AddToPlaylist(Second(state));
        state.SetPlaylistLoops(true);
        state.Cue();
        state.Play();

        state.Director.Tick(3f);

        Assert.False(state.Director.IsFinished);
        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.ScrubHead, 4);
    }

    [Fact]
    public void ALoopingPlaylistWrapsToItsFirstPlayableEntry()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist(state.EditedTrackId);
        state.AddToPlaylist(Second(state));
        state.AddToPlaylist(First(state));
        state.SetPlaylistLoops(true);
        state.Cue();
        state.Play();

        state.Director.Tick(3f);
        Assert.Equal(state.Scene.Playlist[2].Id, state.PlayingEntry!.Id);

        state.Director.Tick(10f);

        Assert.False(state.Director.IsFinished);
        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.ScrubHead, 4);
    }
}
