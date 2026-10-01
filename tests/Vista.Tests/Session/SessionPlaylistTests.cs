using Vista.Core.Scenes;
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

    // Adds Track 2's entry then Track 1's: Track 2's 2 s shot, then Track 1's 10 s.
    private static void TwoEntries(SessionState state)
    {
        state.AddToPlaylist([TrackId(state, 1)]);
        state.AddToPlaylist([TrackId(state, 0)]);
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
        state.SetEntryLoops(Entries(state.Scene)[0].Id, 3);

        var items = state.PlaylistItems(PlaylistEditing.Selected(state.Scene));

        Assert.Equal([Entries(state.Scene)[0].Id, Entries(state.Scene)[2].Id], items.Select(i => i.EntryId));
        Assert.Equal([TrackId(state, 1), TrackId(state, 0)], items.Select(i => i.Track.Id));
        Assert.Equal([3, null], items.Select(i => i.Loops));
    }

    [Fact]
    public void MovingAnUnknownEntrySaysThereIsNoSuchEntry()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist([TrackId(state, 0)]));
        var entry = Entries(state.Scene)[0].Id;

        Assert.Equal("There is no such playlist entry.", state.MoveEntries([Guid.NewGuid()], entry, null));
        Assert.Equal("There is no such playlist entry.", state.MoveEntries([entry], entry, Guid.NewGuid()));
    }

    [Fact]
    public void PlaylistEditsAreUndoSteps()
    {
        var state = Editing();
        Assert.Null(state.AddToPlaylist([TrackId(state, 0)]));
        Assert.Null(state.AddToPlaylist([TrackId(state, 1)], 0));
        var entry = Entries(state.Scene)[1].Id;
        Assert.Null(state.SetEntryLoops(entry, 2));
        Assert.Null(state.MoveEntries([entry], entry, Entries(state.Scene)[0].Id));
        Assert.Null(state.RemoveFromPlaylist([entry]));

        Assert.Single(Entries(state.Scene));
        state.Undo();
        state.Undo();
        Assert.Equal(2, Entries(state.Scene)[1].Loops);
        state.Undo();
        Assert.Null(Entries(state.Scene)[1].Loops);
    }

    [Fact]
    public void DeletingATrackRemovesItsEntriesInOneStep()
    {
        var state = Editing();
        TwoEntries(state);

        state.DeleteTracks([TrackId(state, 1)]);
        Assert.Single(Entries(state.Scene));

        state.Undo();
        Assert.Equal(2, Entries(state.Scene).Count);
    }

    // The entry of the selected playlist Live is playing.
    private static Guid Playing(SessionState state) => state.PlayingEntry!.Id;

    // Seconds through the playlist Live is playing.
    private static double Head(SessionState state) => state.LivePlaylist!.Head;

    [Fact]
    public void LivePlaysThePlaylistsEntriesInTurn()
    {
        // Track 2's 2 s (x = 2t), then Track 1's 10 s (x = 2t): 12 s in all.
        var state = Editing();
        TwoEntries(state);
        GoLive(state);
        Assert.Equal(12.0, state.LivePlaylist!.Timeline!.Total, 1e-4);

        // 1 s in is 1 s into Track 2: x = 2.
        Assert.Equal(2f, state.LiveFrame(1f)!.Value.Position.X, 1e-3f);
        Assert.Equal(EntryId(state, 0), Playing(state));
        Assert.Equal(0, state.LivePlaylist.EntryIndex);
        Assert.Equal(1.0, Head(state), 1e-4);

        // 1.5 s more is 2.5 s in, 0.5 s into Track 1: x = 1.
        Assert.Equal(1f, state.LiveFrame(1.5f)!.Value.Position.X, 1e-3f);
        Assert.Equal(EntryId(state, 1), Playing(state));
        Assert.Equal(1, state.LivePlaylist.EntryIndex);
        Assert.Equal(2.5, Head(state), 1e-4);
    }

    [Fact]
    public void LiveCuesThePlaylistAtItsFirstEntryThatCanPlay()
    {
        // An empty track's entry, then Track 2's and Track 1's: the second entry is the first Live plays.
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        TwoEntries(state);

        Assert.Equal(PlayOutcome.Cued, state.Cue());

        Assert.Equal(EntryId(state, 1), Playing(state));
        Assert.Equal(0, state.LivePlaylist!.EntryIndex);
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Fact]
    public void RestartInLiveGoesBackToTheFirstEntry()
    {
        var state = Editing();
        TwoEntries(state);
        GoLive(state);
        state.LiveFrame(5f);
        Assert.Equal(EntryId(state, 1), Playing(state));

        state.Restart();

        Assert.Equal(EntryId(state, 0), Playing(state));
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Fact]
    public void RestartInLiveSkipsALeadingEntryWithNoPoints()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        state.AddToPlaylist([TrackId(state, 0)]);
        GoLive(state);
        state.LiveFrame(3f);

        state.Restart();

        Assert.Equal(EntryId(state, 1), Playing(state));
        Assert.Equal(0.0, Head(state), 1e-9);
    }

    [Fact]
    public void SettingThePlaylistLoopIsOneUndoStep()
    {
        var state = Editing();

        Assert.Null(state.SetPlaylistLoops(true));
        Assert.True(PlaylistEditing.Selected(state.Scene).Loops);

        state.Undo();
        Assert.False(PlaylistEditing.Selected(state.Scene).Loops);
    }

    [Fact]
    public void LivePlaysTheSelectedPlaylistWithItsLoop()
    {
        // "Intro" holds Track 2 and doesn't loop; the selected playlist, "Main", holds Track 1 (a 10 s shot) and loops.
        var state = Editing();
        var intro = new Playlist(Guid.NewGuid(), "Intro", [new PlaylistEntry(Guid.NewGuid(), TrackId(state, 1))]);
        var main = new Playlist(Guid.NewGuid(), "Main", [new PlaylistEntry(Guid.NewGuid(), TrackId(state, 0))], true);
        state.LoadScene(state.Scene with { Playlists = [intro, main], SelectedPlaylistId = main.Id });
        GoLive(state);

        // 11 s into a looping 10 s playlist is 1 s into its second time round.
        state.LiveFrame(11f);

        Assert.True(state.IsPlaying);
        Assert.Equal(main.Entries[0].Id, Playing(state));
        Assert.Equal(1.0, Head(state), 1e-4);
    }

    [Fact]
    public void ALoopingPlaylistWrapsToItsFirstPlayableEntry()
    {
        var state = Editing();
        state.AddTrack();
        state.AddToPlaylist([state.EditedTrackId]);
        TwoEntries(state);
        state.SetPlaylistLoops(true);
        GoLive(state);

        state.LiveFrame(3f);
        Assert.Equal(EntryId(state, 2), Playing(state));

        // 13 s round the 12 s playlist is 1 s into Track 2's entry, the first that plays.
        state.LiveFrame(10f);

        Assert.True(state.IsPlaying);
        Assert.Equal(EntryId(state, 1), Playing(state));
        Assert.Equal(1.0, Head(state), 1e-4);
    }

    [Fact]
    public void APlaylistSlotPlaysItsOwnPlaylistWithItsLoopWhicheverIsSelected()
    {
        // The selected playlist holds Track 2 and doesn't loop; the slot's, "Main", holds Track 1 (a 10 s shot) and loops.
        var state = Editing();
        var intro = new Playlist(Guid.NewGuid(), "Intro", [new PlaylistEntry(Guid.NewGuid(), TrackId(state, 1))]);
        var main = new Playlist(Guid.NewGuid(), "Main", [new PlaylistEntry(Guid.NewGuid(), TrackId(state, 0))], true);
        state.LoadScene(state.Scene with { Playlists = [intro, main], SelectedPlaylistId = intro.Id });
        Assert.Null(state.SetUseSwitchboard(true));

        Assert.Equal([main.Entries[0].Id], state.PlaylistItems(main).Select(i => i.EntryId));
        state.Cue();
        state.AssignSlot(0, main.Id);
        state.Board!.Click(0);
        state.Board.Cut();
        // 11 s into a looping 10 s playlist is 1 s into its second time round.
        state.LiveFrame(11f);

        Assert.False(state.Board.IsFinished);
        Assert.Equal(main.Entries[0].Id, state.Board.Timeline!.Segments[state.Board.EntryIndex].EntryId);
        Assert.Equal(1.0, state.Board.Head, 4);
    }

    [Fact]
    public void AddingSeveralTracksAddsThemInHierarchyOrder()
    {
        var state = Editing();

        Assert.Null(state.AddToPlaylist([TrackId(state, 1), TrackId(state, 0)]));

        Assert.Equal(new[] { TrackId(state, 0), TrackId(state, 1) }, Entries(state.Scene).Select(e => e.TrackId));
    }

    private static IEnumerable<string> Names(SessionState state) => state.Scene.Playlists.Select(p => p.Name);

    [Fact]
    public void SelectingAPlaylistIsNotAnUndoStep()
    {
        var state = Editing();
        var second = new Playlist(Guid.NewGuid(), "Second", []);
        state.LoadScene(state.Scene with { Playlists = [.. state.Scene.Playlists, second] });
        Assert.False(state.CanUndo);

        Assert.Null(state.SelectPlaylist(second.Id));

        Assert.Equal(second.Id, state.Scene.SelectedPlaylistId);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void SelectingAPlaylistMidDragEndsTheDragAsOneUndoStep()
    {
        var state = Editing();
        var second = new Playlist(Guid.NewGuid(), "Second", []);
        state.LoadScene(state.Scene with { Playlists = [.. state.Scene.Playlists, second] });
        state.BeginLiveEdit();
        state.PreviewPoint(1, Point(20f));

        Assert.Null(state.SelectPlaylist(second.Id));

        // Only an ended drag is on the undo history; a drag still open would leave it empty.
        Assert.True(state.CanUndo);
    }

    [Theory]
    [InlineData(CameraMode.View)]
    [InlineData(CameraMode.Live)]
    public void SelectingAPlaylistIsRefusedOutsideEditing(CameraMode mode)
    {
        var state = Editing();
        state.NewPlaylist("Second");
        var first = state.Scene.Playlists[0].Id;
        state.SelectPlaylist(first);
        TwoEntries(state);
        if (mode == CameraMode.Live)
            GoLive(state);
        else
            state.Release(CameraMode.View);

        Assert.Equal(
            "Playlists can only be switched while editing.",
            state.SelectPlaylist(state.Scene.Playlists[1].Id)
        );
        Assert.Equal(first, state.Scene.SelectedPlaylistId);
    }

    [Fact]
    public void SelectingAnUnknownPlaylistIsRefused()
    {
        var state = Editing();

        Assert.Equal(PlaylistEditing.NoSuchPlaylist, state.SelectPlaylist(Guid.NewGuid()));
    }

    [Fact]
    public void NewPlaylistIsOneUndoStepAndUndoRestoresTheSelection()
    {
        var state = Editing();
        var before = state.Scene.SelectedPlaylistId;

        Assert.Null(state.NewPlaylist("Second"));

        Assert.Equal(["Playlist 1", "Second"], Names(state));
        Assert.Equal("Second", PlaylistEditing.Selected(state.Scene).Name);
        state.Undo();
        Assert.Equal(["Playlist 1"], Names(state));
        Assert.Equal(before, state.Scene.SelectedPlaylistId);
    }

    [Fact]
    public void RenamePlaylistIsOneUndoStep()
    {
        var state = Editing();

        Assert.Null(state.RenamePlaylist(state.Scene.SelectedPlaylistId, "Opening"));

        Assert.Equal(["Opening"], Names(state));
        state.Undo();
        Assert.Equal(["Playlist 1"], Names(state));
    }

    [Fact]
    public void DuplicatePlaylistIsOneUndoStep()
    {
        var state = Editing();
        TwoEntries(state);
        var source = state.Scene.SelectedPlaylistId;

        Assert.Null(state.DuplicatePlaylist(source, "Playlist 1 copy"));

        Assert.Equal(["Playlist 1", "Playlist 1 copy"], Names(state));
        Assert.Equal(2, Entries(state.Scene).Count);
        Assert.NotEqual(source, state.Scene.SelectedPlaylistId);
        state.Undo();
        Assert.Equal(["Playlist 1"], Names(state));
        Assert.Equal(source, state.Scene.SelectedPlaylistId);
    }

    [Fact]
    public void DeletingTheSelectedPlaylistIsOneUndoStepAndUndoBringsItBackSelected()
    {
        var state = Editing();
        TwoEntries(state);
        var first = state.Scene.SelectedPlaylistId;
        state.NewPlaylist("Second");

        Assert.Null(state.SelectPlaylist(first));
        Assert.Null(state.DeletePlaylist(first));

        Assert.Equal(["Second"], Names(state));
        Assert.Equal("Second", PlaylistEditing.Selected(state.Scene).Name);
        state.Undo();
        Assert.Equal(["Playlist 1", "Second"], Names(state));
        Assert.Equal(first, state.Scene.SelectedPlaylistId);
        Assert.Equal(2, Entries(state.Scene).Count);
    }

    [Fact]
    public void DeletingTheLastPlaylistIsRefusedWithoutAnUndoStep()
    {
        var state = Editing();
        var undoable = state.CanUndo;

        Assert.Equal(PlaylistEditing.LastPlaylist, state.DeletePlaylist(state.Scene.SelectedPlaylistId));

        Assert.Equal(["Playlist 1"], Names(state));
        Assert.Equal(undoable, state.CanUndo);
    }

    [Fact]
    public void PlaylistOperationsAreRefusedOutsideEditing()
    {
        var state = Editing();
        state.Release(CameraMode.View);

        Assert.Equal("The scene can only change while editing.", state.NewPlaylist("Second"));
        Assert.Equal(["Playlist 1"], Names(state));
    }

    [Fact]
    public void AddingToThePlaylistAfterSelectingAnotherAddsToTheSelectedOne()
    {
        var state = Editing();
        var first = state.Scene.SelectedPlaylistId;
        state.NewPlaylist("Second");
        Assert.Null(state.SelectPlaylist(first));
        Assert.Null(state.AddToPlaylist([TrackId(state, 0)]));
        state.NewPlaylist("Third");

        Assert.Null(state.SelectPlaylist(state.Scene.Playlists[1].Id));
        Assert.Null(state.AddToPlaylist([TrackId(state, 1)]));

        Assert.Equal([TrackId(state, 1)], Entries(state.Scene).Select(e => e.TrackId));
        Assert.Equal([TrackId(state, 0)], state.Scene.Playlists[0].Entries.Select(e => e.TrackId));
        Assert.Empty(state.Scene.Playlists[2].Entries);
    }
}
