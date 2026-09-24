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
        scene = SceneEditing.Replace(
            scene,
            TrackEditing.Append(TrackEditing.Append(scene.Tracks[0], Point(0f)), Point(10f))
        );
        return SceneEditing.Add(scene).Scene;
    }

    // Adds one entry for track and names it.
    private static (Scene Scene, Guid Added) AddOne(Scene scene, Guid track, int? index = null)
    {
        var result = PlaylistEditing.Add(scene, [track], index);
        return (result, result.Playlist[index ?? result.Playlist.Count - 1].Id);
    }

    [Fact]
    public void ANewSceneHasAnEmptyPlaylist() => Assert.Empty(SceneEditing.New().Playlist);

    [Fact]
    public void AddAppendsOrInsertsAnEntryWithNoLoopCount()
    {
        var scene = TwoTracks();
        var (one, first) = AddOne(scene, scene.Tracks[0].Id);
        var (two, second) = AddOne(one, scene.Tracks[1].Id, 0);

        Assert.Equal(new[] { second, first }, two.Playlist.Select(e => e.Id));
        Assert.Equal(scene.Tracks[1].Id, two.Playlist[0].TrackId);
        Assert.Null(two.Playlist[1].Loops);
        Assert.Equal(Transition.Cut, two.Playlist[1].Transition);
    }

    [Fact]
    public void ATrackCanAppearTwiceAndAddRefusesAMissingTrack()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        scene = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);

        Assert.Equal(2, scene.Playlist.Count);
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Add(scene, [Guid.NewGuid()]));
    }

    [Fact]
    public void RemoveAndReorderChangeTheEntries()
    {
        var scene = TwoTracks();
        var (a, first) = AddOne(scene, scene.Tracks[0].Id);
        var (b, second) = AddOne(a, scene.Tracks[1].Id);

        Assert.Equal(new[] { second, first }, PlaylistEditing.Reorder(b, [1, 0]).Playlist.Select(e => e.Id));
        Assert.Same(b, PlaylistEditing.Reorder(b, [0, 1]));
        Assert.Equal(new[] { second }, PlaylistEditing.Remove(b, [first]).Playlist.Select(e => e.Id));
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

        Assert.Equal(3, PlaylistEditing.SetLoops(a, id, 3).Playlist[0].Loops);
        Assert.Equal(PlaylistEditing.MaxLoops, PlaylistEditing.SetLoops(a, id, 500).Playlist[0].Loops);
        Assert.Equal(1, PlaylistEditing.SetLoops(a, id, 0).Playlist[0].Loops);
        Assert.Null(PlaylistEditing.SetLoops(PlaylistEditing.SetLoops(a, id, 4), id, null).Playlist[0].Loops);
        Assert.Same(a, PlaylistEditing.SetLoops(a, id, null));
    }

    [Fact]
    public void AnEntryHoldsThePlaylistOnlyWithNoCountAndALoopingTrack()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[0], true));
        var (a, id) = AddOne(scene, scene.Tracks[0].Id);

        Assert.True(PlaylistEditing.HoldsPlaylist(a, a.Playlist[0]));
        var counted = PlaylistEditing.SetLoops(a, id, 2);
        Assert.False(PlaylistEditing.HoldsPlaylist(counted, counted.Playlist[0]));
    }

    [Fact]
    public void ALoopingTrackWithNoPointsDoesNotHoldThePlaylist()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[1], true));
        var (a, _) = AddOne(scene, scene.Tracks[1].Id);

        Assert.False(PlaylistEditing.HoldsPlaylist(a, a.Playlist[0]));
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

        Assert.Equal(new[] { scene.Tracks[0].Id }, result.Playlist.Select(e => e.TrackId));
    }

    [Fact]
    public void DuplicatingATrackAddsNoEntry()
    {
        var scene = TwoTracks();
        var withEntry = PlaylistEditing.Add(scene, [scene.Tracks[0].Id]);
        Assert.Single(SceneEditing.Duplicate(withEntry, scene.Tracks[0].Id).Scene.Playlist);
    }

    [Fact]
    public void AddingSeveralInsertsThemInTheOrderGiven()
    {
        var scene = TwoTracks();
        var (one, existing) = AddOne(scene, scene.Tracks[0].Id);

        var result = PlaylistEditing.Add(one, [scene.Tracks[1].Id, scene.Tracks[0].Id], 0);

        Assert.Equal(
            new[] { scene.Tracks[1].Id, scene.Tracks[0].Id, scene.Tracks[0].Id },
            result.Playlist.Select(e => e.TrackId)
        );
        Assert.Equal(existing, result.Playlist[2].Id);
        Assert.Same(one, PlaylistEditing.Add(one, []));
    }

    [Fact]
    public void RemovingSeveralLeavesTheRest()
    {
        var tracks = TwoTracks();
        var full = PlaylistEditing.Add(tracks, [tracks.Tracks[0].Id, tracks.Tracks[1].Id, tracks.Tracks[0].Id]);
        var ids = full.Playlist.Select(e => e.Id).ToArray();

        Assert.Equal(new[] { ids[1] }, PlaylistEditing.Remove(full, [ids[0], ids[2]]).Playlist.Select(e => e.Id));
        Assert.Same(full, PlaylistEditing.Remove(full, []));
    }
}
