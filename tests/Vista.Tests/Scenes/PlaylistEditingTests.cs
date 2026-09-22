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
        scene = SceneEditing.Replace(scene, TrackEditing.Append(TrackEditing.Append(scene.Tracks[0], Point(0f)), Point(10f)));
        return SceneEditing.Add(scene).Scene;
    }

    [Fact]
    public void ANewSceneHasAnEmptyPlaylist() => Assert.Empty(SceneEditing.New().Playlist);

    [Fact]
    public void AddAppendsOrInsertsAnEntryWithNoLoopCount()
    {
        var scene = TwoTracks();
        var (one, first) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);
        var (two, second) = PlaylistEditing.Add(one, scene.Tracks[1].Id, 0);

        Assert.Equal(new[] { second, first }, two.Playlist.Select(e => e.Id));
        Assert.Equal(scene.Tracks[1].Id, two.Playlist[0].TrackId);
        Assert.Null(two.Playlist[1].Loops);
        Assert.Equal(Transition.Cut, two.Playlist[1].Transition);
    }

    [Fact]
    public void ATrackCanAppearTwiceAndAddRefusesAMissingTrack()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;

        Assert.Equal(2, scene.Playlist.Count);
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Add(scene, Guid.NewGuid()));
    }

    [Fact]
    public void RemoveAndMoveChangeTheEntries()
    {
        var scene = TwoTracks();
        var (a, first) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);
        var (b, second) = PlaylistEditing.Add(a, scene.Tracks[1].Id);

        Assert.Equal(new[] { second, first }, PlaylistEditing.Move(b, 0, 1).Playlist.Select(e => e.Id));
        Assert.Same(b, PlaylistEditing.Move(b, 1, 1));
        Assert.Equal(new[] { second }, PlaylistEditing.Remove(b, first).Playlist.Select(e => e.Id));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Remove(b, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => PlaylistEditing.Move(b, 0, 2));
    }

    [Fact]
    public void SetLoopsClampsAndClears()
    {
        var scene = TwoTracks();
        var (a, id) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);

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
        var (a, id) = PlaylistEditing.Add(scene, scene.Tracks[0].Id);

        Assert.True(PlaylistEditing.HoldsPlaylist(a, a.Playlist[0]));
        var counted = PlaylistEditing.SetLoops(a, id, 2);
        Assert.False(PlaylistEditing.HoldsPlaylist(counted, counted.Playlist[0]));
    }

    [Fact]
    public void ALoopingTrackWithNoPointsDoesNotHoldThePlaylist()
    {
        var scene = TwoTracks();
        scene = SceneEditing.Replace(scene, TrackEditing.SetLoop(scene.Tracks[1], true));
        var (a, _) = PlaylistEditing.Add(scene, scene.Tracks[1].Id);

        Assert.False(PlaylistEditing.HoldsPlaylist(a, a.Playlist[0]));
    }

    [Fact]
    public void ThePlaylistCanPlayOnlyWithAnEntryWhoseTrackHasPoints()
    {
        var scene = TwoTracks();
        Assert.False(PlaylistEditing.CanPlay(scene));
        Assert.False(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene));
        Assert.True(PlaylistEditing.CanPlay(PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene));
    }

    [Fact]
    public void DeletingATrackRemovesItsEntries()
    {
        var scene = TwoTracks();
        scene = PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        scene = PlaylistEditing.Add(scene, scene.Tracks[1].Id).Scene;

        var result = SceneEditing.Delete(scene, scene.Tracks[1].Id).Scene;

        Assert.Equal(new[] { scene.Tracks[0].Id }, result.Playlist.Select(e => e.TrackId));
    }

    [Fact]
    public void DuplicatingATrackAddsNoEntry()
    {
        var scene = TwoTracks();
        var withEntry = PlaylistEditing.Add(scene, scene.Tracks[0].Id).Scene;
        Assert.Single(SceneEditing.Duplicate(withEntry, scene.Tracks[0].Id).Scene.Playlist);
    }
}
