using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Scenes;

public class SceneEditingTests
{
    // Three tracks: Track 1, Track 2, Track 3.
    private static Scene Three()
    {
        var scene = SceneEditing.New();
        scene = SceneEditing.Add(scene).Scene;
        return SceneEditing.Add(scene).Scene;
    }

    [Fact]
    public void ANewSceneHoldsOneEmptyTrackNamedTrack1()
    {
        var scene = SceneEditing.New();

        var track = Assert.Single(scene.Tracks);
        Assert.Equal("Track 1", track.Name);
        Assert.Empty(track.Points);
        Assert.Empty(scene.Hidden);
    }

    [Fact]
    public void AddPutsAnEmptyTrackAtTheEndNamedByTheCount()
    {
        var (scene, added) = SceneEditing.Add(SceneEditing.New());

        Assert.Equal(2, scene.Tracks.Count);
        Assert.Equal(added, scene.Tracks[1].Id);
        Assert.Equal("Track 2", scene.Tracks[1].Name);
    }

    [Fact]
    public void AddAfterADeleteCanRepeatAName()
    {
        var scene = Three();
        scene = SceneEditing.Delete(scene, scene.Tracks[0].Id).Scene;
        scene = SceneEditing.Add(scene).Scene;

        Assert.Equal(new[] { "Track 2", "Track 3", "Track 3" }, scene.Tracks.Select(t => t.Name));
    }

    [Fact]
    public void GetAndIndexOfFindTracksById()
    {
        var scene = Three();
        var id = scene.Tracks[2].Id;

        Assert.Equal(2, SceneEditing.IndexOf(scene, id));
        Assert.Same(scene.Tracks[2], SceneEditing.Get(scene, id));
        Assert.Equal(-1, SceneEditing.IndexOf(scene, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => SceneEditing.Get(scene, Guid.NewGuid()));
    }

    [Fact]
    public void ReplaceSwapsTheTrackWithTheSameId()
    {
        var scene = Three();
        var changed = TrackEditing.Append(scene.Tracks[1], Point(5f));
        var result = SceneEditing.Replace(scene, changed);

        Assert.Same(changed, result.Tracks[1]);
        Assert.Same(scene.Tracks[0], result.Tracks[0]);
        Assert.Same(result, SceneEditing.Replace(result, changed));
        Assert.Throws<ArgumentException>(() => SceneEditing.Replace(scene, TrackEditing.Empty()));
    }

    [Fact]
    public void RenameChangesTheNameAndRefusesAnEmptyOne()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var renamed = SceneEditing.Rename(scene, id, "Crane");

        Assert.Equal("Crane", renamed.Tracks[1].Name);
        Assert.Same(renamed, SceneEditing.Rename(renamed, id, "Crane"));
        Assert.Throws<ArgumentException>(() => SceneEditing.Rename(scene, id, ""));
        Assert.Throws<ArgumentException>(() => SceneEditing.Rename(scene, id, "   "));
    }

    [Fact]
    public void RenameTrimsTheName()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;

        var renamed = SceneEditing.Rename(scene, id, "  Crane  ");

        Assert.Equal("Crane", renamed.Tracks[1].Name);
    }

    [Fact]
    public void DuplicateInsertsACopyAfterTheOriginalWithANewIdAndCopyName()
    {
        var scene = Three();
        scene = SceneEditing.Replace(scene, TrackEditing.Append(scene.Tracks[0], Point(4f)));
        var original = scene.Tracks[0];
        var (result, copy) = SceneEditing.Duplicate(scene, original.Id);

        Assert.Equal(4, result.Tracks.Count);
        Assert.Equal(copy, result.Tracks[1].Id);
        Assert.NotEqual(original.Id, copy);
        Assert.Equal("Track 1 copy", result.Tracks[1].Name);
        Assert.Equal(original.Points, result.Tracks[1].Points);
        Assert.Same(scene.Tracks[1], result.Tracks[2]);
    }

    [Fact]
    public void DeleteRemovesTheTrackAndNamesTheOneTakingItsPlace()
    {
        var scene = Three();
        var (result, next) = SceneEditing.Delete(scene, scene.Tracks[1].Id);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(scene.Tracks[2].Id, next);
    }

    [Fact]
    public void DeletingTheLastInTheListNamesTheNewLast()
    {
        var scene = Three();
        var (_, next) = SceneEditing.Delete(scene, scene.Tracks[2].Id);
        Assert.Equal(scene.Tracks[1].Id, next);
    }

    [Fact]
    public void DeleteRefusesTheOnlyTrackAndForgetsAHiddenOne()
    {
        var only = SceneEditing.New();
        Assert.Throws<ArgumentException>(() => SceneEditing.Delete(only, only.Tracks[0].Id));

        var scene = Three();
        var id = scene.Tracks[1].Id;
        scene = SceneEditing.SetHidden(scene, id, true);
        Assert.DoesNotContain(id, SceneEditing.Delete(scene, id).Scene.Hidden);
    }

    [Fact]
    public void ReorderPutsTheTracksInTheOrderGiven()
    {
        var scene = Three();
        var moved = SceneEditing.Reorder(scene, [1, 2, 0]);

        Assert.Equal(new[] { scene.Tracks[1].Id, scene.Tracks[2].Id, scene.Tracks[0].Id }, moved.Tracks.Select(t => t.Id));
        Assert.Same(scene, SceneEditing.Reorder(scene, [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => SceneEditing.Reorder(scene, [0, 1, 3]));
    }

    [Fact]
    public void SetHiddenAddsAndRemovesFromTheHiddenSet()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var hidden = SceneEditing.SetHidden(scene, id, true);

        Assert.Contains(id, hidden.Hidden);
        Assert.Same(hidden, SceneEditing.SetHidden(hidden, id, true));
        Assert.DoesNotContain(id, SceneEditing.SetHidden(hidden, id, false).Hidden);
        Assert.Empty(scene.Hidden);
        Assert.Throws<ArgumentException>(() => SceneEditing.SetHidden(scene, Guid.NewGuid(), true));
    }
}
