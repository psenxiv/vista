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
    public void AddAfterADeleteTakesTheFirstFreeName()
    {
        var scene = Three();
        scene = SceneEditing.Delete(scene, [scene.Tracks[0].Id], scene.Tracks[0].Id).Scene;
        scene = SceneEditing.Add(scene).Scene;

        // Track 1 went, so it is the first of Track 1, Track 2, … that no track has.
        Assert.Equal(new[] { "Track 2", "Track 3", "Track 1" }, scene.Tracks.Select(t => t.Name));
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
    public void TryGetFindsATrackOrSaysThereIsNone()
    {
        var scene = Three();

        Assert.True(SceneEditing.TryGet(scene, scene.Tracks[1].Id, out var track));
        Assert.Same(scene.Tracks[1], track);
        Assert.False(SceneEditing.TryGet(scene, Guid.NewGuid(), out var none));
        Assert.Null(none);
    }

    [Fact]
    public void RequireAllRefusesWhenAnyTrackIsMissing()
    {
        var scene = Three();

        SceneEditing.RequireAll(scene, scene.Tracks.Select(t => t.Id));
        Assert.Equal(
            SceneEditing.NoSuchTrack,
            Assert
                .Throws<ArgumentException>(() => SceneEditing.RequireAll(scene, [scene.Tracks[0].Id, Guid.NewGuid()]))
                .Message
        );
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
    public void RenameChangesTheName()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var renamed = SceneEditing.Rename(scene, id, "Crane");

        Assert.Equal("Crane", renamed.Tracks[1].Name);
        Assert.Same(renamed, SceneEditing.Rename(renamed, id, "Crane"));
    }

    [Fact]
    public void RenameRefusesABlankOrTooLongName()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;

        string Refusal(string name) =>
            Assert.Throws<ArgumentException>(() => SceneEditing.Rename(scene, id, name)).Message;

        Assert.Equal("Enter a name.", Refusal(""));
        Assert.Equal("Enter a name.", Refusal("   "));
        // 65 characters is one past SceneNames.MaxLength.
        Assert.Equal("That name is too long.", Refusal(new string('a', 65)));
    }

    [Fact]
    public void RenameTakesANameNoFileCouldHaveOrOneAnotherTrackHas()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;

        string Renamed(string name) => SceneEditing.Rename(scene, id, name).Tracks[1].Name;

        // 64 characters is SceneNames.MaxLength, counted after trimming.
        Assert.Equal(new string('a', 64), Renamed($"  {new string('a', 64)}  "));
        // File-name rules don't apply to tracks, and neither does another track's name.
        Assert.Equal("a/b?", Renamed("a/b?"));
        Assert.Equal("CON", Renamed("CON"));
        Assert.Equal("Dolly.", Renamed("Dolly."));
        Assert.Equal("Track 3", Renamed("Track 3"));
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
    public void DuplicatingAgainTakesTheNextFreeCopyName()
    {
        var scene = Three();
        var id = scene.Tracks[0].Id;
        scene = SceneEditing.Duplicate(scene, id).Scene;
        scene = SceneEditing.Duplicate(scene, id).Scene;

        // Each copy goes straight after Track 1, so the newest is second; "Track 1 copy" is taken by the first copy.
        Assert.Equal(
            new[] { "Track 1", "Track 1 copy 2", "Track 1 copy", "Track 2", "Track 3" },
            scene.Tracks.Select(t => t.Name)
        );
    }

    [Fact]
    public void ACopysNameMayReachTheLongestNameButNoFurther()
    {
        static Scene Holding(string name)
        {
            var scene = SceneEditing.New();
            return SceneEditing.Rename(scene, scene.Tracks[0].Id, name);
        }

        // A 59-character name gives "name copy", 59 + 5 = 64 characters, the longest allowed.
        var fits = Holding(new string('a', 59));
        var (scene, copy) = SceneEditing.Duplicate(fits, fits.Tracks[0].Id);
        Assert.Equal($"{new string('a', 59)} copy", SceneEditing.Get(scene, copy).Name);

        // A 60-character name would give 60 + 5 = 65 characters.
        var over = Holding(new string('a', 60));
        var refused = Assert.Throws<ArgumentException>(() => SceneEditing.Duplicate(over, over.Tracks[0].Id));
        Assert.Equal("The copy's name would be too long. Shorten the track's name first.", refused.Message);
    }

    [Fact]
    public void DeleteRemovesTheTrackAndNamesTheOneTakingItsPlace()
    {
        var scene = Three();
        var (result, next) = SceneEditing.Delete(scene, [scene.Tracks[1].Id], scene.Tracks[1].Id);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(scene.Tracks[2].Id, next);
    }

    [Fact]
    public void DeletingTheLastInTheListNamesTheNewLast()
    {
        var scene = Three();
        var (_, next) = SceneEditing.Delete(scene, [scene.Tracks[2].Id], scene.Tracks[2].Id);
        Assert.Equal(scene.Tracks[1].Id, next);
    }

    [Fact]
    public void DeleteRefusesTheOnlyTrackAndForgetsAHiddenOne()
    {
        var only = SceneEditing.New();
        Assert.Throws<ArgumentException>(() => SceneEditing.Delete(only, [only.Tracks[0].Id], only.Tracks[0].Id));

        var scene = Three();
        var id = scene.Tracks[1].Id;
        scene = SceneEditing.SetHidden(scene, [id], true);
        Assert.DoesNotContain(id, SceneEditing.Delete(scene, [id], id).Scene.Hidden);
    }

    [Fact]
    public void ReorderPutsTheTracksInTheOrderGiven()
    {
        var scene = Three();
        var moved = SceneEditing.Reorder(scene, [1, 2, 0]);

        Assert.Equal(
            new[] { scene.Tracks[1].Id, scene.Tracks[2].Id, scene.Tracks[0].Id },
            moved.Tracks.Select(t => t.Id)
        );
        Assert.Same(scene, SceneEditing.Reorder(scene, [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => SceneEditing.Reorder(scene, [0, 1, 3]));
    }

    [Fact]
    public void SetHiddenAddsAndRemovesFromTheHiddenSet()
    {
        var scene = Three();
        var id = scene.Tracks[1].Id;
        var hidden = SceneEditing.SetHidden(scene, [id], true);

        Assert.Contains(id, hidden.Hidden);
        Assert.Same(hidden, SceneEditing.SetHidden(hidden, [id], true));
        Assert.DoesNotContain(id, SceneEditing.SetHidden(hidden, [id], false).Hidden);
        Assert.Empty(scene.Hidden);
        Assert.Throws<ArgumentException>(() => SceneEditing.SetHidden(scene, [Guid.NewGuid()], true));
    }

    [Fact]
    public void DeletingSeveralEditsTheFirstRemainingTrackAfterTheEditedOne()
    {
        var scene = SceneEditing.Add(SceneEditing.Add(Three()).Scene).Scene;
        var ids = scene.Tracks.Select(t => t.Id).ToArray();

        // Tracks 2 and 3 go while editing Track 2: Track 4 is the first left after it, not the last, Track 5.
        var (result, edited) = SceneEditing.Delete(scene, [ids[1], ids[2]], ids[1]);

        Assert.Equal(new[] { ids[0], ids[3], ids[4] }, result.Tracks.Select(t => t.Id));
        Assert.Equal(ids[3], edited);
    }

    [Fact]
    public void DeletingSeveralUpToTheEndEditsTheNewLast()
    {
        var scene = SceneEditing.Add(Three()).Scene;
        var ids = scene.Tracks.Select(t => t.Id).ToArray();

        Assert.Equal(ids[1], SceneEditing.Delete(scene, [ids[2], ids[3]], ids[2]).Edited);
    }

    [Fact]
    public void DeletingSeveralKeepsAnEditedTrackThatStays()
    {
        var scene = Three();
        var ids = scene.Tracks.Select(t => t.Id).ToArray();

        Assert.Equal(ids[0], SceneEditing.Delete(scene, [ids[1], ids[2]], ids[0]).Edited);
    }

    [Fact]
    public void DeletingSeveralRemovesTheirEntriesAndHiddenMarks()
    {
        var scene = Three();
        var ids = scene.Tracks.Select(t => t.Id).ToArray();
        scene = SceneEditing.SetHidden(PlaylistEditing.Add(scene, [ids[1], ids[0], ids[2]]), [ids[1], ids[2]], true);

        var result = SceneEditing.Delete(scene, [ids[1], ids[2]], ids[0]).Scene;

        Assert.Equal(new[] { ids[0] }, result.Playlist.Select(e => e.TrackId));
        Assert.Empty(result.Hidden);
    }

    [Fact]
    public void DeletingEveryTrackOrAMissingOneIsRefused()
    {
        var scene = Three();
        var ids = scene.Tracks.Select(t => t.Id).ToArray();

        Assert.Throws<ArgumentException>(() => SceneEditing.Delete(scene, ids, ids[0]));
        Assert.Throws<ArgumentException>(() => SceneEditing.Delete(scene, [ids[1], Guid.NewGuid()], ids[0]));
    }

    [Fact]
    public void SetHiddenChangesSeveralAndLeavesAnUnchangedSceneAlone()
    {
        var scene = Three();
        var ids = scene.Tracks.Select(t => t.Id).ToArray();

        var hidden = SceneEditing.SetHidden(scene, [ids[0], ids[2]], true);

        Assert.True(hidden.Hidden.SetEquals([ids[0], ids[2]]));
        Assert.Equal(new[] { ids[2] }, SceneEditing.SetHidden(hidden, [ids[0], ids[1]], false).Hidden);
        Assert.Same(hidden, SceneEditing.SetHidden(hidden, [ids[0]], true));
        Assert.Throws<ArgumentException>(() => SceneEditing.SetHidden(scene, [Guid.NewGuid()], true));
    }

    // A delete is allowed while some track isn't in it.

    [Fact]
    public void DeletingIsAllowedWhileATrackWouldRemain()
    {
        var scene = SceneEditing.Add(SceneEditing.New()).Scene;
        var (a, b) = (scene.Tracks[0].Id, scene.Tracks[1].Id);

        Assert.True(SceneEditing.CanDelete(scene, [a]));
        Assert.False(SceneEditing.CanDelete(scene, [a, b]));
        var one = SceneEditing.New();
        Assert.False(SceneEditing.CanDelete(one, [one.Tracks[0].Id]));
    }

    // Show needs a hidden track among them; Hide needs a shown one that isn't the edited track.

    [Fact]
    public void ShowNeedsAHiddenTrackAndHideAShownOneOtherThanTheEdited()
    {
        var three = Three();
        var (a, b, c) = (three.Tracks[0].Id, three.Tracks[1].Id, three.Tracks[2].Id);
        var scene = SceneEditing.SetHidden(three, [b], true);

        Assert.True(SceneEditing.CanShow(scene, [a, b]));
        Assert.False(SceneEditing.CanShow(scene, [a]));
        Assert.True(SceneEditing.CanHide(scene, [a, c], a));
        Assert.False(SceneEditing.CanHide(scene, [a], a));
        Assert.False(SceneEditing.CanHide(scene, [b], a));
    }

    [Fact]
    public void OthersShownLeaveOutTheEditedTrackAndHiddenOnes()
    {
        var three = Three();
        var (a, b, c) = (three.Tracks[0].Id, three.Tracks[1].Id, three.Tracks[2].Id);

        var others = SceneEditing.OthersShown(SceneEditing.SetHidden(three, [b], true), a);

        Assert.Equal([c], others.Select(t => t.Id));
    }
}
