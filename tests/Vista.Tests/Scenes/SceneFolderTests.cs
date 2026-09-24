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
