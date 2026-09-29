using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class PlaylistEditingTests
{
    // Two tracks; the first has two points, the second none.
    private static Scene TwoTracks()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Replace(scene, WithTwoPoints(scene.Tracks[0]));
        return SceneEditing.Add(scene).Scene;
    }

    // Adds one entry for track and names it.
    private static (Scene Scene, Guid Added) AddOne(Scene scene, Guid track, int? index = null)
    {
        var result = PlaylistEditing.Add(scene, [track], index);
        return (result, Entries(result)[index ?? Entries(result).Count - 1].Id);
    }

    // TwoTracks with a first playlist playing Track 1 then Track 2, and an empty second playlist, selected.
    private static (Scene Scene, Playlist First) SecondSelected()
    {
        var scene = TwoTracks();
        var first = PlaylistEditing.Selected(PlaylistEditing.Add(scene, [scene.Tracks[0].Id, scene.Tracks[1].Id]));
        var second = new Playlist(Guid.NewGuid(), "Playlist 2", []);
        return (scene with { Playlists = [first, second], SelectedPlaylistId = second.Id }, first);
    }

    [Fact]
    public void EditsActOnTheSelectedPlaylistAndLeaveTheOtherAlone()
    {
        var (scene, first) = SecondSelected();
        var tracks = scene.Tracks.Select(t => t.Id).ToArray();

        // The first playlist could play Track 1's points, but the selected one is empty.
        Assert.False(PlaylistEditing.CanPlay(scene));
        Assert.Equal(-1, PlaylistEditing.IndexOf(scene, first.Entries[0].Id));
        scene = PlaylistEditing.Add(scene, [tracks[0], tracks[1]]);
        Assert.True(PlaylistEditing.CanPlay(scene));
        scene = PlaylistEditing.Reorder(scene, [1, 0]);
        scene = PlaylistEditing.SetPlaylistLoops(scene, true);

        Assert.Equal(new[] { tracks[1], tracks[0] }, Entries(scene).Select(e => e.TrackId));
        Assert.True(PlaylistEditing.Selected(scene).Loops);
        Assert.Same(first, scene.Playlists[0]);
    }

    [Fact]
    public void RemoveAndSetLoopsReachAnEntryInAPlaylistThatIsntSelected()
    {
        var (scene, first) = SecondSelected();
        var (one, two) = (first.Entries[0].Id, first.Entries[1].Id);

        Assert.Equal(4, PlaylistEditing.SetLoops(scene, one, 4).Playlists[0].Entries[0].Loops);
        var removed = PlaylistEditing.Remove(scene, [two]);
        Assert.Equal(new[] { one }, removed.Playlists[0].Entries.Select(e => e.Id));
        Assert.Empty(Entries(removed));
    }

    [Fact]
    public void AddAppendsOrInsertsAnEntryWithNoLoopCount()
    {
        var scene = TwoTracks();
        var (one, first) = AddOne(scene, scene.Tracks[0].Id);
        var (two, second) = AddOne(one, scene.Tracks[1].Id, 0);

        Assert.Equal(new[] { second, first }, Entries(two).Select(e => e.Id));
        Assert.Equal(scene.Tracks[1].Id, Entries(two)[0].TrackId);
        Assert.Null(Entries(two)[1].Loops);
        Assert.Equal(Transition.Cut, Entries(two)[1].Transition);
    }

    [Fact]
    public void ATrackCanAppearTwiceAndAddRefusesAMissingTrack()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);

        Assert.Equal(2, Entries(scene).Count);
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Add(scene, [Guid.NewGuid()]));
    }

    [Fact]
    public void RemoveAndReorderChangeTheEntries()
    {
        var scene = TwoTracks();
        var (a, first) = AddOne(scene, scene.Tracks[0].Id);
        var (b, second) = AddOne(a, scene.Tracks[1].Id);

        Assert.Equal(new[] { second, first }, Entries(PlaylistEditing.Reorder(b, [1, 0])).Select(e => e.Id));
        Assert.Same(b, PlaylistEditing.Reorder(b, [0, 1]));
        Assert.Equal(new[] { second }, Entries(PlaylistEditing.Remove(b, [first])).Select(e => e.Id));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Remove(b, [Guid.NewGuid()]));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Reorder(b, [0, 0]));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData(" 12 ", 12)]
    [InlineData("500", PlaylistEditing.MaxLoops)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("0", null)]
    [InlineData("-4", null)]
    public void ATypedCountSetsClampsOrClears(string text, int? expected)
    {
        Assert.True(PlaylistEditing.ParseLoops(text, out var loops));
        Assert.Equal(expected, loops);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void TypingSomethingOtherThanAWholeNumberChangesNothing(string text)
    {
        Assert.False(PlaylistEditing.ParseLoops(text, out _));
    }

    [Fact]
    public void SetLoopsClampsAndClears()
    {
        var scene = TwoTracks();
        var (a, id) = AddOne(scene, scene.Tracks[0].Id);

        Assert.Equal(3, Entries(PlaylistEditing.SetLoops(a, id, 3))[0].Loops);
        Assert.Equal(PlaylistEditing.MaxLoops, Entries(PlaylistEditing.SetLoops(a, id, 500))[0].Loops);
        Assert.Equal(1, Entries(PlaylistEditing.SetLoops(a, id, 0))[0].Loops);
        Assert.Null(Entries(PlaylistEditing.SetLoops(PlaylistEditing.SetLoops(a, id, 4), id, null))[0].Loops);
        Assert.Same(a, PlaylistEditing.SetLoops(a, id, null));
    }

    [Fact]
    public void AnEntryHoldsThePlaylistOnlyWithNoCountAndALoopingTrack()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[0], true));
        var (a, id) = AddOne(scene, scene.Tracks[0].Id);

        Assert.True(PlaylistEditing.HoldsPlaylist(a, Entries(a)[0]));
        var counted = PlaylistEditing.SetLoops(a, id, 2);
        Assert.False(PlaylistEditing.HoldsPlaylist(counted, Entries(counted)[0]));
    }

    [Fact]
    public void ALoopingTrackWithNoPointsDoesNotHoldThePlaylist()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[1], true));
        var (a, _) = AddOne(scene, scene.Tracks[1].Id);

        Assert.False(PlaylistEditing.HoldsPlaylist(a, Entries(a)[0]));
    }

    [Fact]
    public void ThePlaylistCanPlayOnlyWithAnEntryWhoseTrackHasPoints()
    {
        var scene = TwoTracks();
        Assert.False(PlaylistEditing.CanPlay(scene));
        Assert.False(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, [scene.Tracks[1].Id])));
        Assert.True(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, [scene.Tracks[0].Id])));
    }

    [Fact]
    public void DeletingATrackRemovesItsEntries()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, [scene.Tracks[1].Id]);
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        scene = PlaylistEditing.Add(scene, [scene.Tracks[1].Id]);

        var result = SceneEditing.Delete(scene, [scene.Tracks[1].Id], scene.Tracks[0].Id).Scene;

        Assert.Equal(new[] { scene.Tracks[0].Id }, Entries(result).Select(e => e.TrackId));
    }

    [Fact]
    public void DuplicatingATrackAddsNoEntry()
    {
        var scene = TwoTracks();
        var withEntry = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        Assert.Single(Entries(SceneEditing.Duplicate(withEntry, scene.Tracks[0].Id).Scene));
    }

    [Fact]
    public void AddingSeveralInsertsThemInTheOrderGiven()
    {
        var scene = TwoTracks();
        var (one, existing) = AddOne(scene, scene.Tracks[0].Id);

        var result = PlaylistEditing.Add(one, [scene.Tracks[1].Id, scene.Tracks[0].Id], 0);

        Assert.Equal(
            new[] { scene.Tracks[1].Id, scene.Tracks[0].Id, scene.Tracks[0].Id },
            Entries(result).Select(e => e.TrackId)
        );
        Assert.Equal(existing, Entries(result)[2].Id);
        Assert.Same(one, PlaylistEditing.Add(one, []));
    }

    [Fact]
    public void RemovingSeveralLeavesTheRest()
    {
        var tracks = TwoTracks();
        var full = PlaylistEditing.Add(tracks, [tracks.Tracks[0].Id, tracks.Tracks[1].Id, tracks.Tracks[0].Id]);
        var ids = Entries(full).Select(e => e.Id).ToArray();

        Assert.Equal(new[] { ids[1] }, Entries(PlaylistEditing.Remove(full, [ids[0], ids[2]])).Select(e => e.Id));
        Assert.Same(full, PlaylistEditing.Remove(full, []));
    }

    // A count above 0 reads as itself; 0 follows the track, forever when that holds the playlist, else once.

    [Theory]
    [InlineData(3, false, Repeats.Count)]
    [InlineData(3, true, Repeats.Count)]
    [InlineData(0, true, Repeats.Forever)]
    [InlineData(0, false, Repeats.Once)]
    public void ARepeatCountReadsAsACountForeverOrOnce(int loops, bool holds, Repeats expected) =>
        Assert.Equal(expected, PlaylistEditing.RepeatsOf(loops, holds));

    // Up adds one, stopping at 99; down takes one, and down from 1 or from none follows the track.

    [Theory]
    [InlineData(null, true, 1)]
    [InlineData(4, true, 5)]
    [InlineData(99, true, 99)]
    [InlineData(4, false, 3)]
    [InlineData(1, false, null)]
    [InlineData(null, false, null)]
    public void AWheelNotchStepsTheCountByOne(int? loops, bool up, int? expected) =>
        Assert.Equal(expected, PlaylistEditing.StepLoops(loops, up));

    // A scene whose playlists are named as given, in that order, with the first selected.
    private static Scene WithNames(params string[] names)
    {
        var playlists = names.Select(n => new Playlist(Guid.NewGuid(), n, [])).ToArray();
        return SceneEditing.New() with { Playlists = playlists, SelectedPlaylistId = playlists[0].Id };
    }

    private static Guid IdOf(Scene scene, string name) => scene.Playlists.Single(p => p.Name == name).Id;

    private static Playlist Get(Scene scene, Guid id) => PlaylistEditing.Get(scene, id);

    // A new scene holds "Playlist 1", so the next free name is "Playlist 2".
    [Fact]
    public void ANewSuggestionSkipsTheNamesInUse() =>
        Assert.Equal("Playlist 2", PlaylistEditing.NewSuggestion(SceneEditing.New()));

    [Fact]
    public void NewAppendsATrimmedEmptyPlaylistAndSelectsIt()
    {
        var scene = SceneEditing.New();

        var result = PlaylistEditing.New(scene, " Intro ");

        Assert.Equal(["Playlist 1", "Intro"], result.Playlists.Select(p => p.Name));
        var added = PlaylistEditing.Selected(result);
        Assert.Equal("Intro", added.Name);
        Assert.Empty(added.Entries);
        Assert.False(added.Loops);
        Assert.Same(scene.Playlists[0], result.Playlists[0]);
        Assert.NotEqual(scene.SelectedPlaylistId, added.Id);
    }

    [Fact]
    public void NewRefusesATakenOrBlankName()
    {
        var scene = SceneEditing.New();

        Assert.Equal(
            "A playlist with that name exists.",
            Assert.Throws<ArgumentException>(() => PlaylistEditing.New(scene, "playlist 1")).Message
        );
        Assert.Equal("Enter a name.", Assert.Throws<ArgumentException>(() => PlaylistEditing.New(scene, "")).Message);
    }

    [Fact]
    public void RenameAllowsACaseChangeButNotAnotherPlaylistsName()
    {
        var scene = WithNames("Alpha", "Beta");
        var alpha = IdOf(scene, "Alpha");

        var recased = PlaylistEditing.Rename(scene, alpha, "ALPHA");
        Assert.Equal(["ALPHA", "Beta"], recased.Playlists.Select(p => p.Name));
        Assert.Equal(
            PlaylistEditing.NameTaken,
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Rename(scene, alpha, " beta ")).Message
        );
    }

    [Fact]
    public void RenameToTheSameTrimmedNameReturnsTheSameScene()
    {
        var scene = WithNames("Alpha", "Beta");

        Assert.Same(scene, PlaylistEditing.Rename(scene, IdOf(scene, "Alpha"), " Alpha "));
    }

    [Fact]
    public void RenameRefusesAnUnknownPlaylist()
    {
        var scene = WithNames("Alpha");

        Assert.Equal(
            "There is no such playlist.",
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Rename(scene, Guid.NewGuid(), "Beta")).Message
        );
    }

    [Fact]
    public void ACopySuggestionAppendsCopy()
    {
        var scene = SceneEditing.New();

        Assert.Equal("Playlist 1 copy", PlaylistEditing.CopySuggestion(scene, scene.SelectedPlaylistId));
        Assert.Equal(
            "There is no such playlist.",
            Assert.Throws<ArgumentException>(() => PlaylistEditing.CopySuggestion(scene, Guid.NewGuid())).Message
        );
    }

    // Two entries on Track 1 and Track 2 (repeating 3 times), in a looping playlist.
    private static Scene LoopingTwoEntries()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id, scene.Tracks[1].Id]);
        scene = PlaylistEditing.SetLoops(scene, Entries(scene)[1].Id, 3);
        return PlaylistEditing.SetPlaylistLoops(scene, true);
    }

    [Fact]
    public void DuplicateCopiesEntriesUnderNewIdsAndSelectsTheCopyWhenTheSourceWasSelected()
    {
        var scene = LoopingTwoEntries();
        var source = PlaylistEditing.Selected(scene);

        var result = PlaylistEditing.Duplicate(scene, source.Id, " Copy ");

        Assert.Equal([source.Name, "Copy"], result.Playlists.Select(p => p.Name));
        var copy = PlaylistEditing.Selected(result);
        Assert.Equal("Copy", copy.Name);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(source.Entries.Select(e => e.TrackId), copy.Entries.Select(e => e.TrackId));
        Assert.Equal(new int?[] { null, 3 }, copy.Entries.Select(e => e.Loops));
        Assert.Empty(source.Entries.Select(e => e.Id).Intersect(copy.Entries.Select(e => e.Id)));
        Assert.True(copy.Loops);
        Assert.Same(source, result.Playlists[0]);
    }

    [Fact]
    public void DuplicateKeepsTheSelectionWhenTheSourceWasNotSelected()
    {
        var scene = LoopingTwoEntries();
        var source = PlaylistEditing.Selected(scene);
        scene = PlaylistEditing.New(scene, "Other");

        var result = PlaylistEditing.Duplicate(scene, source.Id, "Copy");

        Assert.Equal("Other", PlaylistEditing.Selected(result).Name);
        Assert.Equal(["Playlist 1", "Other", "Copy"], result.Playlists.Select(p => p.Name));
    }

    [Fact]
    public void DuplicateRefusesATakenNameAndAnUnknownPlaylist()
    {
        var scene = SceneEditing.New();

        Assert.Equal(
            PlaylistEditing.NameTaken,
            Assert
                .Throws<ArgumentException>(() =>
                    PlaylistEditing.Duplicate(scene, scene.SelectedPlaylistId, "PLAYLIST 1")
                )
                .Message
        );
        Assert.Equal(
            PlaylistEditing.NoSuchPlaylist,
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Duplicate(scene, Guid.NewGuid(), "Copy")).Message
        );
    }

    // Removing an entry of the copy must not touch the source, which would happen if they shared entry ids.
    [Fact]
    public void RemovingACopiedEntryLeavesTheSourceUntouched()
    {
        var scene = LoopingTwoEntries();
        var source = PlaylistEditing.Selected(scene);
        var result = PlaylistEditing.Duplicate(scene, source.Id, "Copy");
        var copiedEntry = PlaylistEditing.Selected(result).Entries[0].Id;

        var removed = PlaylistEditing.Remove(result, [copiedEntry]);

        Assert.Equal(source.Entries.Select(e => e.Id), Get(removed, source.Id).Entries.Select(e => e.Id));
        Assert.Single(Get(removed, PlaylistEditing.Selected(result).Id).Entries);
    }

    [Fact]
    public void TheLastPlaylistCantBeDeleted()
    {
        var scene = SceneEditing.New();

        Assert.False(PlaylistEditing.CanDelete(scene));
        Assert.Equal(
            "The last playlist can't be deleted.",
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Delete(scene, scene.SelectedPlaylistId)).Message
        );
        Assert.True(PlaylistEditing.CanDelete(PlaylistEditing.New(scene, "Two")));
    }

    // Stored as b, C, a with b selected: ignoring case the order is a, b, C, so deleting b selects a.
    [Fact]
    public void DeletingTheSelectedPlaylistSelectsTheFirstByNameIgnoringCase()
    {
        var scene = WithNames("b", "C", "a");

        var result = PlaylistEditing.Delete(scene, IdOf(scene, "b"));

        Assert.Equal(["C", "a"], result.Playlists.Select(p => p.Name));
        Assert.Equal(IdOf(scene, "a"), result.SelectedPlaylistId);
    }

    // b is selected; deleting C leaves b and a, where a is first by name, so only a kept selection stays on b.
    [Fact]
    public void DeletingAnUnselectedPlaylistKeepsTheSelection()
    {
        var scene = WithNames("b", "C", "a");

        var result = PlaylistEditing.Delete(scene, IdOf(scene, "C"));

        Assert.Equal(["b", "a"], result.Playlists.Select(p => p.Name));
        Assert.Equal(IdOf(scene, "b"), result.SelectedPlaylistId);
    }

    // Same name in two cases: the first stored wins the tie.
    [Fact]
    public void DeletingSelectsTheFirstStoredWhenNamesTie()
    {
        var scene = WithNames("x", "Dup", "dup");
        scene = PlaylistEditing.Select(scene, IdOf(scene, "x"));

        var result = PlaylistEditing.Delete(scene, IdOf(scene, "x"));

        Assert.Equal(IdOf(scene, "Dup"), result.SelectedPlaylistId);
    }

    [Fact]
    public void DeleteRefusesAnUnknownPlaylist()
    {
        var scene = WithNames("a", "b");

        Assert.Equal(
            PlaylistEditing.NoSuchPlaylist,
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Delete(scene, Guid.NewGuid())).Message
        );
    }

    // Hand-edited files can hold two playlists with one name: a third can't take it in any case, and either can be renamed by case.
    [Fact]
    public void NameRefusalWithDuplicateNamesPresent()
    {
        var scene = WithNames("Dup", "Dup");

        Assert.Equal(PlaylistEditing.NameTaken, PlaylistEditing.NameRefusal(scene, "dup"));
        Assert.Null(PlaylistEditing.NameRefusal(scene, "DUP", scene.Playlists[0].Id));
        Assert.Null(PlaylistEditing.NameRefusal(scene, "Other"));
        Assert.Equal("Enter a name.", PlaylistEditing.NameRefusal(scene, "  "));
    }

    [Fact]
    public void SelectRefusesAnUnknownPlaylistAndReturnsTheSameSceneWhenAlreadySelected()
    {
        var scene = WithNames("a", "b");

        Assert.Equal(
            PlaylistEditing.NoSuchPlaylist,
            Assert.Throws<ArgumentException>(() => PlaylistEditing.Select(scene, Guid.NewGuid())).Message
        );
        Assert.Same(scene, PlaylistEditing.Select(scene, scene.SelectedPlaylistId));
        Assert.Equal(IdOf(scene, "b"), PlaylistEditing.Select(scene, IdOf(scene, "b")).SelectedPlaylistId);
    }
}
