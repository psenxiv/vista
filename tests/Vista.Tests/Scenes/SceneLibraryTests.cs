using Vista.Core.Scenes;
using Vista.Core.Session;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Scenes.SceneFixtures;

namespace Vista.Tests.Scenes;

public sealed class SceneLibraryTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly TempFolder other = new();
    private readonly SessionState state = new();
    private readonly SceneLibrary library;
    private readonly SceneLibrary next;

    public SceneLibraryTests()
    {
        state.Edit();
        library = new SceneLibrary(temp.Folder, () => state.Scene, state.LoadScene, new UnreadableNotices());
        next = new SceneLibrary(other.Folder, () => state.Scene, state.LoadScene, new UnreadableNotices());
    }

    public void Dispose()
    {
        temp.Dispose();
        other.Dispose();
    }

    private string EditedTrackName => state.Scene.Tracks[0].Name;

    private void RenameFirstTrack(string name) => Assert.Null(state.RenameTrack(state.Scene.Tracks[0].Id, name));

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
    public void OpeningByItselfSkipsScenesFromANewerVista()
    {
        // Aurora is the last scene and first by name, but a newer Vista saved it.
        File.WriteAllText(Path.Combine(temp.Scenes, "Aurora.json"), "{ \"format\": 3 }");
        Save("Dawn", "Crane");

        Assert.Null(library.Open("Aurora"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
    }

    [Fact]
    public void OpeningWhenTheLastSceneCantBeReadSaysWhichSceneOpenedInstead()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");

        Assert.Null(library.Open("Broken"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Could not read Broken, so Dawn is open instead. The file may be damaged.", library.Notice);
    }

    [Fact]
    public void TheNoticeNamesTheFileAsItIsSpelledOnDisk()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");

        library.Open("broken");

        Assert.Equal("Could not read Broken, so Dawn is open instead. The file may be damaged.", library.Notice);
    }

    [Fact]
    public void TheNoticeNamesTheNewSceneWhenNothingElseCanBeRead()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");

        Assert.Null(library.Open("Broken"));

        // No scene is readable, so Open makes the first free "Scene N", which is Scene 1.
        Assert.Equal("Could not read Broken, so Scene 1 is open instead. The file may be damaged.", library.Notice);
    }

    [Fact]
    public void OpeningTheLastSceneGivesNoNotice()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");

        library.Open("Dawn");

        Assert.Null(library.Notice);
    }

    [Fact]
    public void OpeningWithNoLastSceneGivesNoNoticeEvenWithAnUnreadableFile()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");

        library.Open(null);

        Assert.Null(library.Notice);
    }

    [Fact]
    public void OpeningWhenTheLastSceneIsGoneGivesNoNotice()
    {
        Save("Dawn", "Crane");

        library.Open("Gone");

        Assert.Null(library.Notice);
    }

    [Fact]
    public void OpeningWhenTheLastSceneIsFromANewerVistaGivesNoNotice()
    {
        File.WriteAllText(temp.ScenePath("Future"), "{ \"format\": 3 }");
        Save("Dawn", "Crane");

        library.Open("Future");

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Null(library.Notice);
    }

    [Fact]
    public void TheNoticeGoesWithTheNextOpen()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");
        library.Open("Broken");

        library.Open("Dawn");

        Assert.Null(library.Notice);
    }

    [Fact]
    public void TheStartupNoticeCountsAsTellingThePlayerAboutThatFile()
    {
        var notices = new UnreadableNotices();
        var told = new SceneLibrary(temp.Folder, () => state.Scene, state.LoadScene, notices);
        File.WriteAllText(temp.ScenePath("Broken"), "{");
        Save("Dawn", "Crane");

        told.Open("Broken");

        Assert.Null(notices.Unlisted("Broken"));
    }

    [Fact]
    public void OpeningASceneFromANewerVistaAsksForANewerVista()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        File.WriteAllText(Path.Combine(temp.Scenes, "Future.json"), "{ \"format\": 3 }");

        Assert.Equal(
            "Could not open Future: This scene needs a newer version of Vista. Update Vista to open it.",
            library.Switch("Future")
        );
        Assert.Equal("Dawn", library.CurrentName);
    }

    [Fact]
    public void AnOlderSceneOpenedWithoutABackupIsOnlySavedOnceItsBackupIsWritten()
    {
        temp.WriteFormatOne("Harbour");
        temp.BlockBackups();
        Assert.Null(library.Open("Harbour"));
        RenameFirstTrack("Jib");

        Assert.StartsWith("Could not save Harbour:", library.SaveNow());

        File.Delete(temp.Folder.BackupsDir);
        Assert.Null(library.SaveNow());
        Assert.Equal("Jib", FirstTrackIn(temp, "Harbour"));
        var backup = Assert.Single(Directory.GetFiles(temp.Folder.BackupsDir, "*", SearchOption.AllDirectories));
        Assert.Equal(FormatOneSceneJson(), File.ReadAllText(backup));
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
    public void SwitchingToTheOpenSceneKeepsUndo()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Switch("Dawn"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
        Assert.True(state.CanUndo);
        // Neither saved nor reloaded: the file on disk still has the name from before the rename.
        Assert.Equal("Crane", FirstTrackIn(temp, "Dawn"));
    }

    [Fact]
    public void SwitchingToTheOpenSceneInAnotherCaseKeepsUndo()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Switch("DAWN"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
        Assert.True(state.CanUndo);
        Assert.Equal("Crane", FirstTrackIn(temp, "Dawn"));
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
        LiveTwoPoints(state);
        Assert.Equal(CameraMode.Live, state.Mode);

        Assert.Equal("A scene can't be loaded while Live.", library.Switch("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Crane", EditedTrackName);
    }

    [Theory]
    [InlineData("  ", "Enter a name.")]
    [InlineData("a/b", "That name can't be used as a file name.")]
    [InlineData("dusk", "A scene with that name exists.")]
    [InlineData(" Broken ", "A scene with that name exists.")]
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
    [InlineData("dusk", "A scene with that name exists.")]
    [InlineData("Dawn.", "That name can't be used as a file name.")]
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
    public void RenamingAnotherSceneRenamesItsFileAndLeavesTheOpenSceneAlone()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Rename("Dusk", "Night"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
        Assert.Equal(["Dawn.json", "Night.json"], temp.SceneFiles());
        Assert.Equal("Dolly", FirstTrackIn(temp, "Night"));
    }

    [Fact]
    public void RenamingAnotherSceneRefusesATakenName()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Equal("A scene with that name exists.", library.Rename("Dusk", "Dawn"));

        Assert.Equal(["Dawn.json", "Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void RenamingAnotherSceneRefusesABadName()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Equal("That name can't be used as a file name.", library.Rename("Dusk", "a/b"));

        Assert.Equal(["Dawn.json", "Dusk.json"], temp.SceneFiles());
    }

    [Fact]
    public void DuplicatingAnotherSceneCopiesItsFileWithoutOpeningItOrTouchingTheOpenScene()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        RenameFirstTrack("Jib");

        Assert.Null(library.Duplicate("Dusk", "Dusk copy"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("Jib", EditedTrackName);
        Assert.Equal("Dolly", FirstTrackIn(temp, "Dusk copy"));
        // The open scene's own file is untouched: it was saved with "Crane" and never re-saved.
        Assert.Equal("Crane", FirstTrackIn(temp, "Dawn"));
        // The open scene was never reloaded, so its undo history (the rename above) still stands.
        Assert.True(state.CanUndo);
    }

    [Fact]
    public void DuplicatingAnUnreadableSceneIsRefusedAndWritesNoCopy()
    {
        Save("Dawn", "Crane");
        File.WriteAllText(Path.Combine(temp.Scenes, "Broken.json"), "{");
        library.Open("Dawn");

        var refusal = library.Duplicate("Broken", "Broken copy");

        Assert.StartsWith("Could not open Broken: ", refusal, StringComparison.Ordinal);
        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal(["Broken.json", "Dawn.json"], temp.SceneFiles());
    }

    [Fact]
    public void DuplicatingAnotherSceneRefusesATakenName()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Equal("A scene with that name exists.", library.Duplicate("Dusk", "Dawn"));
    }

    [Fact]
    public void DeletingAnotherSceneDeletesOnlyItsFile()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Null(library.Delete("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal(["Dawn.json"], temp.SceneFiles());
    }

    [Fact]
    public void DeletingAnotherSceneIsRefusedWhenTheFolderHasGone()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");
        Directory.Delete(temp.Scenes, recursive: true);

        Assert.StartsWith("Could not delete Dusk:", library.Delete("Dusk"));

        Assert.Equal("Dawn", library.CurrentName);
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
        LiveTwoPoints(state);
        Assert.Equal(CameraMode.Live, state.Mode);

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

        Assert.StartsWith("Could not delete Dawn:", library.Delete());
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
        temp.Folder.SaveScene("Dawn", SceneEditing.SetHidden(scene, [scene.Tracks[0].Id], true));
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
    public void SuggestionsSkipUnreadableFiles()
    {
        Save("Scene 1", "Crane");
        File.WriteAllText(Path.Combine(temp.Scenes, "Scene 2.json"), "{");
        File.WriteAllText(Path.Combine(temp.Scenes, "Scene 1 copy.json"), "{");
        library.Open("Scene 1");

        // Scene 1 is read and Scene 2 isn't, but both are taken; so is Scene 1 copy.
        Assert.Equal("Scene 3", library.NewSuggestion());
        Assert.Equal("Scene 1 copy 2", library.CopySuggestion());
        Assert.Null(library.New(library.NewSuggestion()));
    }

    [Fact]
    public void CopySuggestionByNameSkipsUnreadableFilesForAnyScene()
    {
        Save("Scene 1", "Crane");
        File.WriteAllText(Path.Combine(temp.Scenes, "Scene 2.json"), "{");
        File.WriteAllText(Path.Combine(temp.Scenes, "Scene 1 copy.json"), "{");
        library.Open("Scene 1");

        // Scene 1 copy is taken by the unreadable file, so the open scene's suggestion skips to copy 2.
        Assert.Equal("Scene 1 copy 2", library.CopySuggestion("Scene 1"));
        // Scene 2 copy is free, even though Scene 2 itself is only picked from the unreadable listing.
        Assert.Equal("Scene 2 copy", library.CopySuggestion("Scene 2"));
    }

    [Fact]
    public void NameRefusalCountsUnreadableFilesAndLetsARenameKeepItsName()
    {
        Save("Dawn", "Crane");
        File.WriteAllText(Path.Combine(temp.Scenes, "Broken.json"), "{");
        library.Open("Dawn");

        Assert.Equal("A scene with that name exists.", library.NameRefusal(" broken "));
        Assert.Equal("A scene with that name exists.", library.NameRefusal("DAWN"));
        Assert.Null(library.NameRefusal("DAWN", renaming: "Dawn"));
        Assert.Equal("A scene with that name exists.", library.NameRefusal("Broken", renaming: "Dawn"));
        Assert.Null(library.NameRefusal("Dusk"));
    }

    [Fact]
    public void NameRefusalAllowsTheNameBeingRenamedForAnyScene()
    {
        Save("Dawn", "Crane");
        Save("Dusk", "Dolly");
        library.Open("Dawn");

        Assert.Null(library.NameRefusal("DUSK", renaming: "Dusk"));
        Assert.Equal("A scene with that name exists.", library.NameRefusal("Dawn", renaming: "Dusk"));
    }

    [Fact]
    public void RecreatingIsRefusedWhenAFileHasTheFoldersName()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Directory.Delete(temp.Folder.Root, recursive: true);
        File.WriteAllText(temp.Folder.Root, "");

        Assert.StartsWith("Could not create the save folder:", library.Recreate());
        Assert.Equal("Dawn", library.CurrentName);
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

    [Fact]
    public void ChoosingAFolderForTheFirstTimeReopensAtTheLastScene() =>
        Assert.Equal(FolderChange.Reopen, SceneLibrary.Change(null, temp.Parent, ready: false, sceneOpen: false));

    [Fact]
    public void ChoosingTheOpenFolderAgainKeepsIt() =>
        Assert.Equal(
            FolderChange.Keep,
            SceneLibrary.Change(temp.Parent, temp.Parent.ToUpperInvariant(), ready: true, sceneOpen: true)
        );

    [Fact]
    public void ChoosingTheLostFolderWithASceneOpenRecreatesIt() =>
        Assert.Equal(
            FolderChange.Recreate,
            SceneLibrary.Change(temp.Parent, temp.Parent, ready: false, sceneOpen: true)
        );

    [Fact]
    public void ChoosingTheLostFolderWithNoSceneOpenReopensIt() =>
        Assert.Equal(
            FolderChange.Reopen,
            SceneLibrary.Change(temp.Parent, temp.Parent, ready: false, sceneOpen: false)
        );

    [Fact]
    public void ChoosingAnotherFolderMoves() =>
        Assert.Equal(
            FolderChange.Move,
            SceneLibrary.Change(temp.Parent, Path.Combine(temp.Parent, "other"), ready: true, sceneOpen: true)
        );

    [Fact]
    public void MovingSavesTheOpenSceneHereAndOpensTheNewFoldersFirstScene()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");
        other.Folder.SaveScene("Alpha", Named("Dolly"));

        Assert.Equal((true, null), library.MoveTo(next));

        Assert.Equal("Jib", FirstTrackIn(temp, "Dawn"));
        Assert.Equal("Alpha", next.CurrentName);
        Assert.Equal("Dolly", EditedTrackName);
    }

    [Fact]
    public void MovingFromAGoneFolderCarriesTheOpenSceneAndKeepsUndo()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        RenameFirstTrack("Jib");
        Directory.Delete(temp.Folder.Root, recursive: true);

        Assert.Equal((true, null), library.MoveTo(next));

        Assert.Equal("Dawn", next.CurrentName);
        Assert.Equal(["Dawn.json"], other.SceneFiles());
        Assert.Equal("Jib", FirstTrackIn(other, "Dawn"));
        Assert.True(state.CanUndo);
    }

    [Fact]
    public void MovingFromAGoneFolderCarriesASceneSavedBeforeItWent()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Assert.Null(library.SaveNow());
        Directory.Delete(temp.Folder.Root, recursive: true);

        Assert.Equal((true, null), library.MoveTo(next));

        // Nothing changed since the last save, but that file went with the folder.
        Assert.Equal("Crane", FirstTrackIn(other, "Dawn"));
        Assert.Equal("Dawn", next.CurrentName);
    }

    [Fact]
    public void ACarriedSceneNeverOverwritesOneOfItsName()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        other.Folder.SaveScene("Dawn", Named("Dolly"));
        Directory.Delete(temp.Folder.Root, recursive: true);

        Assert.Equal((true, null), library.MoveTo(next));

        // SceneNames.CopyOf appends " copy" to the name, then a number, the first not taken
        // (Numbered("Dawn copy", existing)). Only "Dawn" exists in other's folder, so "Dawn copy" is free.
        const string copy = "Dawn copy";
        Assert.Equal(copy, next.CurrentName);
        Assert.Equal("Dolly", FirstTrackIn(other, "Dawn"));
        Assert.Equal("Crane", FirstTrackIn(other, copy));
    }

    [Fact]
    public void ACarriedSceneAutosavesUnderItsNewName()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        other.Folder.SaveScene("Dawn", Named("Dolly"));
        Directory.Delete(temp.Folder.Root, recursive: true);
        Assert.Equal((true, null), library.MoveTo(next));
        RenameFirstTrack("Jib");

        // The change is first seen at 10 s, so it is due at 10 + SaveDebounce.DelaySeconds (1 s) = 11 s.
        Assert.Null(next.Tick(10.0));
        Assert.Null(next.Tick(11.0));

        Assert.Equal("Jib", FirstTrackIn(other, "Dawn copy"));
        Assert.Equal("Dolly", FirstTrackIn(other, "Dawn"));
    }

    [Fact]
    public void ACarriedSceneCountsAsSaved()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Directory.Delete(temp.Folder.Root, recursive: true);
        Assert.Equal((true, null), library.MoveTo(next));
        var file = Path.Combine(other.Scenes, "Dawn.json");
        File.Delete(file);

        // Nothing changed since the carry wrote it, so nothing writes it again.
        Assert.Null(next.Tick(0.0));
        Assert.Null(next.Tick(5.0));
        Assert.Null(next.SaveNow());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void MovingIsRefusedWhenTheSceneCanBeSavedInNeitherFolder()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Directory.Delete(temp.Folder.Root, recursive: true);
        Directory.Delete(other.Folder.Root, recursive: true);

        var (moved, refusal) = library.MoveTo(next);

        Assert.False(moved);
        Assert.StartsWith("Could not save Dawn:", refusal);
        Assert.Equal("Dawn", library.CurrentName);
        Assert.Equal("", next.CurrentName);
    }

    [Fact]
    public void MovingWithNoSceneOpenOpensTheNewFolder()
    {
        other.Folder.SaveScene("Alpha", Named("Dolly"));

        Assert.Equal((true, null), library.MoveTo(next));

        Assert.Equal("Alpha", next.CurrentName);
    }

    [Fact]
    public void ASelectedPlaylistIsSaved()
    {
        Save("Dawn", "Crane");
        library.Open("Dawn");
        Assert.Null(state.NewPlaylist("Second"));
        Assert.Null(library.SaveNow());
        var first = state.Scene.Playlists[0].Id;

        Assert.Null(state.SelectPlaylist(first));
        Assert.Null(library.SaveNow());

        Assert.Equal(first, temp.Folder.LoadScene("Dawn").SelectedPlaylistId);
    }
}
