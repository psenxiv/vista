using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Session.SessionFixtures;

namespace Vista.Tests.Session;

public class SessionPlaylistTests
{
    // Editing; Track 1 has points at x = 0, 10, 20 (a 10 s shot at 2 yalms per second); Track 2 has points at x = 0, 4 (2 s).
    private static SessionState Editing()
    {
        var state = EditingThreePoints();
        state.AddTrack();
        state.SetTrackSpeed(2f);
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(4f));
        return state;
    }

    [Fact]
    public void PlaylistItemsAreTheEntriesWithPointsInOrder()
    {
        // Entries for Track 2 (3 times), an empty third track, then Track 1: the empty one can't play.
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([state.Scene.Tracks[2].Id]);
        state.AddToPlaylist([TrackId(state, 0)]);
        state.SetEntryLoops(state.Scene.Playlist[0].Id, 3);

        var items = state.PlaylistItems();

        Assert.Equal([state.Scene.Playlist[0].Id, state.Scene.Playlist[2].Id], items.Select(i => i.EntryId));
        Assert.Equal([TrackId(state, 1), TrackId(state, 0)], items.Select(i => i.Track.Id));
        Assert.Equal([3, null], items.Select(i => i.Loops));
    }

    [Fact]
    public void MovingAnUnknownEntrySaysThereIsNoSuchEntry()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist([TrackId(state, 0)]));
        var entry = state.Scene.Playlist[0].Id;

        Assert.Equal("There is no such playlist entry.", state.MoveEntries([Guid.NewGuid()], entry, null));
        Assert.Equal("There is no such playlist entry.", state.MoveEntries([entry], entry, Guid.NewGuid()));
    }

    [Fact]
    public void PlaylistEditsAreUndoSteps()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist([TrackId(state, 0)]));
        Assert.Null(state.AddToPlaylist([TrackId(state, 1)], 0));
        var entry = state.Scene.Playlist[1].Id;
        Assert.Null(state.SetEntryLoops(entry, 2));
        Assert.Null(state.MoveEntries([entry], entry, state.Scene.Playlist[0].Id));
        Assert.Null(state.RemoveFromPlaylist([entry]));

        Assert.Single(state.Scene.Playlist);
        state.Undo();
        state.Undo();
        Assert.Equal(2, state.Scene.Playlist[1].Loops);
        state.Undo();
        Assert.Null(state.Scene.Playlist[1].Loops);
    }

    [Fact]
    public void DeletingATrackRemovesItsEntriesInOneStep()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);

        state.DeleteTracks([TrackId(state, 1)]);
        Assert.Single(state.Scene.Playlist);

        state.Undo();
        Assert.Equal(2, state.Scene.Playlist.Count);
    }

    [Fact]
    public void LiveIsRefusedWhenNothingCanPlay()
    {
        var state = Editing();
        Assert.Equal(PlayOutcome.Refused, state.Cue());
    }

    [Fact]
    public void PlayAndRestartFromViewAreRefusedWhenNothingCanPlay()
    {
        var state = Editing();
        state.Release(CameraMode.View);

        Assert.Equal(PlayOutcome.Refused, state.Play());
        Assert.Equal(CameraMode.View, state.Mode);

        Assert.Equal(PlayOutcome.Refused, state.Restart());
        Assert.Equal(CameraMode.View, state.Mode);
    }

    [Fact]
    public void LiveCuesThePlaylistAtItsFirstPlayableEntryAndPlaysItInTurn()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);

        Assert.Equal(PlayOutcome.Cued, state.Cue());
        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(2.0, state.Transport.ScrubLength, 4);

        state.Play();
        state.Director.Tick(3f);
        Assert.Equal(state.Scene.Playlist[2].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.Transport.ScrubHead, 4);
        Assert.Equal(10.0, state.Transport.ScrubLength, 4);
    }

    [Fact]
    public void ScrubbingLiveSeeksWithinThePlayingEntry()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);
        state.Director.Tick(3f);

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(7.0);
        state.Transport.EndScrub();

        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(7.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void ScrubbingPastThePlayingEntrysLengthClampsAndStaysOnIt()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);

        state.Transport.BeginScrub();
        state.Transport.ScrubTo(99.0);
        state.Transport.EndScrub();

        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(2.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void TheEndHoldsAndPlayStartsAgain()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        GoLive(state);
        state.Director.Tick(5f);

        Assert.True(state.Director.IsFinished);
        Assert.Equal(CameraMode.Live, state.Mode);
        Assert.Equal(2.0, state.Transport.ScrubHead, 4);

        Assert.Equal(PlayOutcome.Started, state.Play());
        Assert.Equal(0.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void RestartInLiveGoesBackToTheFirstEntry()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);
        state.Director.Tick(5f);

        state.Restart();

        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(0.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void RestartInLiveSkipsALeadingEntryWithNoPoints()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);
        state.Director.Tick(3f);

        state.Restart();

        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(0.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void EditFromLiveTakesTheShotTimeOnlyWhenTheEditedTrackIsPlaying()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);
        state.Director.Tick(1f);

        state.Edit();
        Assert.Equal(1.0, state.Transport.ScrubHead, 4);

        state.SwitchTrack(TrackId(state, 0));
        GoLive(state);
        state.Director.Tick(1f);
        state.Edit();
        Assert.Equal(0.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void PlayingEntryIsNullUnlessLive()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 0)]);
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
    public void LivePlaysALoopingPlaylistRoundAgain()
    {
        var state = Editing();
        state.AddToPlaylist([TrackId(state, 1)]);
        state.SetPlaylistLoops(true);
        GoLive(state);

        state.Director.Tick(3f);

        Assert.False(state.Director.IsFinished);
        Assert.Equal(state.Scene.Playlist[0].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void ALoopingPlaylistWrapsToItsFirstPlayableEntry()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
        state.SetPlaylistLoops(true);
        GoLive(state);

        state.Director.Tick(3f);
        Assert.Equal(state.Scene.Playlist[2].Id, state.PlayingEntry!.Id);

        state.Director.Tick(10f);

        Assert.False(state.Director.IsFinished);
        Assert.Equal(state.Scene.Playlist[1].Id, state.PlayingEntry!.Id);
        Assert.Equal(1.0, state.Transport.ScrubHead, 4);
    }

    [Fact]
    public void AddingSeveralTracksAddsThemInHierarchyOrder()
    {
        var state = Editing();

        Assert.Null(state.AddToPlaylist([TrackId(state, 1), TrackId(state, 0)]));

        Assert.Equal(new[] { TrackId(state, 0), TrackId(state, 1) }, state.Scene.Playlist.Select(e => e.TrackId));
    }
}
