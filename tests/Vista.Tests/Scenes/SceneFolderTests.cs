using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Scenes.SceneFixtures;

namespace Vista.Tests.Scenes;

public sealed class SceneFolderTests : IDisposable
{
    private readonly TempFolder temp = new();

    private SceneFolder Folder => temp.Folder;

    public void Dispose() => temp.Dispose();

    [Fact]
    public void FileErrorsAreMissingLockedOrForbiddenFilesAndUnreadableAddsBadContent()
    {
        Assert.True(SceneFolder.IsFileError(new FileNotFoundException()));
        Assert.True(SceneFolder.IsFileError(new UnauthorizedAccessException()));
        Assert.False(SceneFolder.IsFileError(new InvalidDataException()));
        Assert.True(SceneFolder.IsUnreadable(new InvalidDataException()));
        Assert.True(SceneFolder.IsUnreadable(new IOException()));
        Assert.False(SceneFolder.IsUnreadable(new ArgumentException()));
    }

    [Fact]
    public void TheRootIsVistaxivInsideTheParent()
    {
        Assert.Equal(Path.Combine("parent", "vistaxiv"), SceneFolder.RootFor("parent"));
    }

    [Fact]
    public void ExistsNeedsTheRootScenesAndPresets()
    {
        Assert.True(Folder.Exists);

        Directory.Delete(temp.Presets);
        Assert.False(Folder.Exists);
        Folder.Create();
        Assert.True(Directory.Exists(temp.Presets));
        Assert.True(Folder.Exists);

        Directory.Delete(temp.Scenes);
        Assert.False(Folder.Exists);

        Directory.Delete(Folder.Root, recursive: true);
        Assert.False(Folder.Exists);
        Folder.Create();
        Assert.True(Directory.Exists(temp.Scenes));
        Assert.True(Directory.Exists(temp.Presets));
    }

    [Fact]
    public void AddingASceneWritesItUnderItsName()
    {
        Assert.True(Folder.AddScene("Demo - Limsa", DemoSceneJson()));

        Assert.Equal(["Demo - Limsa.json"], temp.SceneFiles());
        Assert.Equal(DemoSceneJson(), File.ReadAllText(Path.Combine(temp.Scenes, "Demo - Limsa.json")));
    }

    [Fact]
    public void AddingASceneLeavesOneWithTheSameNameAloneWhateverItsCase()
    {
        Folder.SaveScene("demo - limsa", Named("Mine"));

        Assert.False(Folder.AddScene("Demo - Limsa", DemoSceneJson()));

        Assert.Equal(["demo - limsa.json"], temp.SceneFiles());
        Assert.Equal("Mine", FirstTrackIn(temp, "demo - limsa"));
    }

    [Fact]
    public void TheShippedDemoSceneReads()
    {
        Assert.Equal(5, DemoScene().Tracks.Count);
    }

    [Fact]
    public void ASavedSceneLoadsBack()
    {
        var scene = Named("Crane") with { PlaylistLoops = true };
        Folder.SaveScene("Dusk", scene);

        var read = Folder.LoadScene("Dusk");
        Assert.Equal(scene.Tracks[0].Id, read.Tracks[0].Id);
        Assert.Equal("Crane", read.Tracks[0].Name);
        Assert.True(read.PlaylistLoops);
    }

    [Fact]
    public void SavingReplacesTheFileAndLeavesNoTemporary()
    {
        Folder.SaveScene("Dusk", Named("Crane"));
        Folder.SaveScene("Dusk", Named("Dolly"));

        Assert.Equal(["Dusk.json"], temp.SceneFiles());
        Assert.Equal("Dolly", FirstTrackIn(temp, "Dusk"));
    }

    [Fact]
    public void SceneNamesAreSortedIgnoringCase()
    {
        Folder.SaveScene("Beta", Named("B"));
        Folder.SaveScene("alpha", Named("A"));
        Folder.SaveScene("gamma", Named("G"));

        // Ordinal order would put "Beta" first, since 'B' (66) sorts before 'a' (97).
        Assert.Equal(["alpha", "Beta", "gamma"], Folder.SceneNames());
    }

    [Fact]
    public void UnreadableScenesAreReportedAndLeftOut()
    {
        Folder.SaveScene("Good", Named("Crane"));
        File.WriteAllText(Path.Combine(temp.Scenes, "Garbage.json"), "{ not json");
        File.WriteAllText(Path.Combine(temp.Scenes, "Future.json"), "{ \"format\": 2 }");
        File.WriteAllText(Path.Combine(temp.Scenes, "notes.txt"), "not a scene");
        File.WriteAllText(Path.Combine(temp.Scenes, "Half.json.tmp"), "{");

        Assert.Equal(["Good"], Folder.SceneNames());
        Assert.Equal(["Future.json", "Garbage.json"], temp.Unreadable.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AMissingScenesFolderListsNoScenes()
    {
        Directory.Delete(temp.Scenes);
        Assert.Empty(Folder.SceneNames());
        Assert.Empty(temp.Unreadable);
    }

    [Fact]
    public void RenamingMovesTheFile()
    {
        Folder.SaveScene("Dusk", Named("Crane"));
        Folder.RenameScene("Dusk", "Dawn");

        Assert.Equal(["Dawn.json"], temp.SceneFiles());
        Assert.Equal("Crane", FirstTrackIn(temp, "Dawn"));
    }

    [Fact]
    public void ACaseOnlyRenameChangesTheFileName()
    {
        Folder.SaveScene("dusk", Named("Crane"));
        Folder.RenameScene("dusk", "Dusk");

        Assert.Equal(["Dusk.json"], temp.SceneFiles());
        Assert.Equal("Crane", FirstTrackIn(temp, "Dusk"));
    }

    [Fact]
    public void DeletingRemovesTheFile()
    {
        Folder.SaveScene("Dusk", Named("Crane"));
        Folder.SaveScene("Dawn", Named("Dolly"));
        Folder.DeleteScene("Dusk");

        Assert.Equal(["Dawn.json"], temp.SceneFiles());
    }

    [Fact]
    public void SceneEntriesListNameTrackCountAndModifiedTime()
    {
        var before = DateTime.Now;
        Folder.SaveScene("Dusk", Named("Crane"));
        Folder.SaveScene("Dawn", DemoScene());
        var after = DateTime.Now;

        var entries = Folder.SceneEntries();

        Assert.Equal(2, entries.Count);
        var dawn = entries[0];
        var dusk = entries[1];
        Assert.Equal("Dawn", dawn.Name);
        // DemoScene has five tracks (see TheShippedDemoSceneReads).
        Assert.Equal(5, dawn.Tracks);
        Assert.Equal("Dusk", dusk.Name);
        // Named() makes a scene with a single track.
        Assert.Equal(1, dusk.Tracks);
        Assert.InRange(dawn.Modified, before, after);
        Assert.InRange(dusk.Modified, before, after);
    }

    [Fact]
    public void SceneEntriesSkipUnreadableFiles()
    {
        Folder.SaveScene("Good", Named("Crane"));
        File.WriteAllText(Path.Combine(temp.Scenes, "Garbage.json"), "{ not json");

        Assert.Equal(["Good"], Folder.SceneEntries().Select(e => e.Name));
        Assert.Equal(["Garbage.json"], temp.Unreadable);
    }

    [Fact]
    public void PresetEntriesHaveNoTrackCount()
    {
        var before = DateTime.Now;
        Folder.SavePreset("Orbit", new Preset(TrackEditing.Empty(), 0f));
        var after = DateTime.Now;

        var entry = Assert.Single(Folder.PresetEntries());

        Assert.Equal("Orbit", entry.Name);
        Assert.Null(entry.Tracks);
        Assert.InRange(entry.Modified, before, after);
    }

    [Fact]
    public void PresetEntriesSkipUnreadableFiles()
    {
        Folder.SavePreset("Orbit", new Preset(TrackEditing.Empty(), 0f));
        File.WriteAllText(Path.Combine(temp.Presets, "Broken.json"), "[]");

        Assert.Equal(["Orbit"], Folder.PresetEntries().Select(e => e.Name));
        Assert.Equal(["Broken.json"], temp.Unreadable);
    }

    [Fact]
    public void APresetLoadsBackNamedAfterItsFile()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(name: "Old name"), Point(3f, 4f, 5f));
        Folder.SavePreset("Orbit", new Preset(track, 0.75f));

        var read = Folder.LoadPreset("Orbit");
        Assert.Equal("Orbit", read.Track.Name);
        Assert.Equal(0.75f, read.Yaw, 0f);
        Assert.Equal(Point(3f, 4f, 5f), Assert.Single(read.Track.Points));
        Assert.Equal(["Orbit.json"], Directory.GetFiles(temp.Presets).Select(f => Path.GetFileName(f)));
    }

    [Fact]
    public void PresetNamesAreSortedAndSkipUnreadableFiles()
    {
        var preset = new Preset(TrackEditing.Empty(), 0f);
        Folder.SavePreset("Orbit", preset);
        Folder.SavePreset("dolly", preset);
        File.WriteAllText(Path.Combine(temp.Presets, "Broken.json"), "[]");

        // Ordinal order would put "Orbit" first, since 'O' (79) sorts before 'd' (100).
        Assert.Equal(["dolly", "Orbit"], Folder.PresetNames());
        Assert.Equal(["Broken.json"], temp.Unreadable);
    }

    [Fact]
    public void DeletingAPresetRemovesItsFile()
    {
        var preset = new Preset(TrackEditing.Empty(), 0f);
        Folder.SavePreset("Orbit", preset);
        Folder.SavePreset("Dolly", preset);
        Folder.DeletePreset("Orbit");

        Assert.Equal(["Dolly"], Folder.PresetNames());
        Assert.Equal(["Dolly.json"], Directory.GetFiles(temp.Presets).Select(f => Path.GetFileName(f)));
    }
}
