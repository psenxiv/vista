using Vista.Core.Scenes;
using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Scenes.SceneFixtures;

namespace Vista.Tests.Scenes;

public sealed class SceneLibraryTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly SessionState state = new();
    private readonly SceneLibrary library;

    public SceneLibraryTests()
    {
        state.Edit();
        library = new SceneLibrary(temp.Folder, () => state.Scene, state.LoadScene);
    }

    public void Dispose() => temp.Dispose();

    private string EditedTrackName => state.Scene.Tracks[0].Name;

    private void RenameFirstTrack(string name) => Assert.Null(state.RenameTrack(state.Scene.Tracks[0].Id, name));

    private void GoLive()
    {
        state.AddToEnd(Point(0f));
        state.AddToEnd(Point(10f));
        state.AddToPlaylist(state.Scene.Tracks[0].Id);
        state.Cue();
        state.Play();
        Assert.Equal(CameraMode.Live, state.Mode);
    }

    private void Save(string scene, string trackName) => temp.Folder.SaveScene(scene, Named(trackName));

    [Fact]
    public void OpeningAnEmptyFolderMakesScene1()
    {
        Assert.Null(library.Open("Gone"));

        Assert.Equal("Scene 1", library.CurrentName);
        Assert.Equal(["Scene 1.json"], temp.SceneFiles());
        Assert.Equal("Track 1", Assert.Single(state.Scene.Tracks).Name);
        Assert.Equal("Track 1", FirstTrackIn(temp, "Scene 1"));
    }

    [Fact]
    public void OpeningNeverOverwritesAnUnreadableFile()
    {
        File.WriteAllText(Path.Combine(temp.Scenes, "Scene 1.json"), "{");

        Assert.Null(library.Open(null));

        // Scene 1 is taken by the unreadable file, so the next free name is Scene 2.
        Assert.Equal("Scene 2", library.CurrentName);
        Assert.Equal("{", File.ReadAllText(Path.Combine(temp.Scenes, "Scene 1.json")));
    }

    [Fact]
    public void OpeningPicksTheLastSceneIgnoringCase()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");

        Assert.Null(library.Open("dusk"));

        Assert.Equal("Dusk", library.CurrentName);
        Assert.Equal("Dolly", EditedTrackName);
    }

    [Fact]
    public void OpeningFallsBackToTheFirstSceneByName()
    {
        Save("dusk", "Dolly");
        Save("Dawn", "Crane");

        Assert.Null(library.Open("Gone"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
    }

    [Fact]
    public void SwitchingSavesTheOpenSceneThenLoads()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Switch("Dusk"));

        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
        Assert.Equal("Dusk", library.CurrentName);
        Assert.Equal("Dolly", EditedTrackName);
    }

    [Fact]
    public void SwitchingIsRefusedWhenTheSaveFails()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        RenameFirstTrack("Jib");
        Directory.Delete(temp.Scenes, recursive: true);

        Assert.StartsWith("Could not save Dawn:", library.Switch("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
    }

    [Fact]
    public void SwitchingToAnUnreadableSceneIsRefused()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        File.WriteAllText(Path.Combine(temp.Scenes, "Dusk.json"), "{");

        Assert.StartsWith("Could not open Dusk:", library.Switch("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
    }

    [Fact]
    public void ARefusedLoadIsPassedThrough()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        GoLive();

        Assert.Equal("A scene can't be loaded while Live.", library.Switch("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
    }

    [Theory]
    [InlineData("  ", "Enter a name")]
    [InlineData("a/b", "That name can't be used as a file name")]
    [InlineData("dusk", "A scene with that name exists")]
    [InlineData(" Broken ", "A scene with that name exists")]
    public void NewAndDuplicateRefuseBadNames(string name, string refusal)
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        File.WriteAllText(Path.Combine(temp.Scenes, "Broken.json"), "{");
        library.Open("Dawn");

        Assert.Equal(refusal, library.New(name));
        Assert.Equal(refusal, library.Duplicate(name));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal(["Broken.json", "Dawn.json", "Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void NewSavesTheOpenSceneAndOpensAnEmptyOne()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.New("  Night  "));

        Assert.Equal("Night", library.CurrentName);
        Assert.Equal(["Dawn.json", "Night.json"], temp.SceneFiles());
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
        Assert.Equal("Track 1", FirstTrackIn(temp, "Night"));
        Assert.Equal("Track 1", Assert.Single(state.Scene.Tracks).Name);
    }

    [Fact]
    public void NewIsRefusedWhenTheSaveFails()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");
        Directory.Delete(temp.Scenes, recursive: true);

        Assert.StartsWith("Could not save Dawn:", library.New("Night"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
    }

    [Fact]
    public void RenamingMovesTheFileAndKeepsUndo()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Rename(" Dusk "));

        Assert.Equal("Dusk", library.CurrentName);
        Assert.Equal(["Dusk.json"], temp.SceneFiles());
        Assert.True(state.CanUndo);

        library.SaveNow();
        Assert.Equal("Jib", FirstTrackIn(temp, "Dusk"));
        Assert.Equal(["Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void RenamingToTheSameNameInAnotherCaseIsAllowed()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");

        Assert.Null(library.Rename("DAWN"));
        Assert.Equal("DAWN", library.CurrentName);
        Assert.Equal(["DAWN.json"], temp.SceneFiles());

        Assert.Null(library.Rename("DAWN"));
        Assert.Equal(["DAWN.json"], temp.SceneFiles());
    }

    [Theory]
    [InlineData("dusk", "A scene with that name exists")]
    [InlineData("Dawn.", "That name can't be used as a file name")]
    public void RenamingRefusesBadNames(string name, string refusal)
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Equal(refusal, library.Rename(name));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal(["Dawn.json", "Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void DuplicatingSavesACopyAndOpensIt()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Duplicate("Dawn copy"));

        Assert.Equal("Dawn copy", library.CurrentName);
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn copy"));
        Assert.Equal("Jib", EditedTrackName);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void DeletingOpensTheFirstRemainingSceneThenMakesScene1()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dusk");

        Assert.Null(library.Delete());
        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
        Assert.Equal(["Dawn.json"], temp.SceneFiles());

        Assert.Null(library.Delete());
        Assert.Equal("Scene 1", library.CurrentName);
        Assert.Equal(["Scene 1.json"], temp.SceneFiles());
        Assert.Equal("Track 1", Assert.Single(state.Scene.Tracks).Name);
    }

    [Fact]
    public void ARefusedOpenAfterDeletingLeavesNoSceneToSave()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        GoLive();

        Assert.Equal("A scene can't be loaded while Live.", library.Delete());

        Assert.Equal("", library.CurrentName);
        Assert.Null(library.SaveNow());
        Assert.Equal(["Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void DeletingIsRefusedWhenTheFolderHasGone()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Directory.Delete(temp.Scenes, recursive: true);

        Assert.StartsWith("Could not save Dawn:", library.Delete());
        Assert.Equal("Dawn", library.CurrentName);
    }

    [Fact]
    public void SaveNowWritesOnlyAChangedScene()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        var file = Path.Combine(temp.Scenes, "Dawn.json");

        File.Delete(file);
        Assert.Null(library.SaveNow());
        Assert.False(File.Exists(file));

        RenameFirstTrack("Jib");
        Assert.Null(library.SaveNow());
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));

        File.Delete(file);
        Assert.Null(library.SaveNow());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void TickSavesOnceTheChangeIsDue()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        // The change is first seen at 10 s, so it is due at 10 + SaveDebounce.DelaySeconds (1 s) = 11 s.
        Assert.Null(library.Tick(10.0));
        Assert.Null(library.Tick(10.9));
        Assert.Equal("Crane", FirstTrackIn(temp, "Dawn"));

        Assert.Null(library.Tick(11.0));
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
    }

    [Fact]
    public void LoadingCountsTheSessionsSceneAsSaved()
    {
        var scene = Named("Crane");
        temp.Folder.SaveScene("Dawn", SceneEditing.SetHidden(scene, scene.Tracks[0].Id, true));
        library.Open("Dawn");
        var file = Path.Combine(temp.Scenes, "Dawn.json");
        File.Delete(file);

        // LoadScene shows the hidden first track, so the session holds a new scene that must count as saved.
        Assert.Null(library.Tick(0.0));
        Assert.Null(library.Tick(5.0));
        Assert.Null(library.SaveNow());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void NameRefusalCountsUnreadableFilesAndLetsARenameKeepItsName()
    {
        Save("Dawn", "Crane");
        File.WriteAllText(Path.Combine(temp.Scenes, "Broken.json"), "{");
        library.Open("Dawn");

        Assert.Equal("A scene with that name exists", library.NameRefusal(" broken "));
        Assert.Equal("A scene with that name exists", library.NameRefusal("DAWN"));
        Assert.Null(library.NameRefusal("DAWN", renaming: true));
        Assert.Equal("A scene with that name exists", library.NameRefusal("Broken", renaming: true));
        Assert.Null(library.NameRefusal("Dusk"));
    }

    [Fact]
    public void RecreatingPutsTheOpenSceneBackAndKeepsUndo()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");
        Assert.Null(library.SaveNow());
        Directory.Delete(temp.Folder.Root, recursive: true);

        Assert.Null(library.Recreate());

        // Written even though nothing changed since the last save, since the file went with the folder.
        Assert.True(temp.Folder.Exists);
        Assert.Equal(["Dawn.json"], temp.SceneFiles());
        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
        Assert.Equal("Dawn", library.CurrentName);
        Assert.True(state.CanUndo);
    }
}
