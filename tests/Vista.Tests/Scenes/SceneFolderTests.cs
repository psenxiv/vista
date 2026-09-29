using System.Text.Json.Nodes;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Scenes.SceneFixtures;

namespace Vista.Tests.Scenes;

public sealed class SceneFolderTests : IDisposable
{
    private readonly TempFolder temp;

    // Backups are named for this time: 2026-09-29 12:08:49 unless a test moves it on.
    private DateTime now = new(2026, 9, 29, 12, 8, 49);

    public SceneFolderTests() => temp = new TempFolder(() => now);

    private SceneFolder Folder => temp.Folder;

    // The backup folder for format 1 files at the first time: v{format}-{yyyy-MM-dd_HH-mm-ss}.
    private string Backups => Path.Combine(Folder.BackupsDir, "v1-2026-09-29_12-08-49");

    private void WriteFormatOne(string name) => temp.WriteFormatOne(name);

    // The backup folder names, sorted ordinally.
    private IEnumerable<string> BackupFolders() =>
        Directory.GetDirectories(Folder.BackupsDir).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal);

    // A scene file in the current format but not as Vista lays it out, so a rewrite would change its bytes.
    private void WriteCompact(string name) =>
        File.WriteAllText(temp.ScenePath(name), JsonNode.Parse(SceneJson.Write(Named("Crane")))!.ToJsonString());

    public void Dispose() => temp.Dispose();

    [Fact]
    public void FileErrorsAreMissingLockedOrForbiddenFilesAndUnreadableAddsBadContent()
    {
        Assert.True(SceneFolder.IsFileError(new FileNotFoundException()));
        Assert.True(SceneFolder.IsFileError(new UnauthorizedAccessException()));
        Assert.False(SceneFolder.IsFileError(new InvalidDataException()));
        Assert.True(SceneFolder.IsUnreadable(new InvalidDataException()));
        Assert.True(SceneFolder.IsUnreadable(new NewerFormatException()));
        Assert.True(SceneFolder.IsUnreadable(new IOException()));
        Assert.False(SceneFolder.IsUnreadable(new ArgumentException()));
    }

    [Fact]
    public void TheRootIsVistaxivInsideTheParent()
    {
        Assert.Equal(Path.Combine("parent", "vistaxiv"), SceneFolder.RootFor("parent"));
    }

    [Fact]
    public void TheSameParentIgnoresCaseAndATrailingSeparator()
    {
        var parent = Directory.CreateTempSubdirectory("vista-tests-").FullName;
        try
        {
            Assert.True(SceneFolder.SameParent(parent, parent.ToUpperInvariant() + Path.DirectorySeparatorChar));
            Assert.False(SceneFolder.SameParent(parent, Path.Combine(parent, "other")));
        }
        finally
        {
            Directory.Delete(parent);
        }
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
        var scene = PlaylistEditing.SetPlaylistLoops(Named("Crane"), true);
        Folder.SaveScene("Dusk", scene);

        var read = Folder.LoadScene("Dusk");
        Assert.Equal(scene.Tracks[0].Id, read.Tracks[0].Id);
        Assert.Equal("Crane", read.Tracks[0].Name);
        Assert.True(PlaylistEditing.Selected(read).Loops);
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
        File.WriteAllText(Path.Combine(temp.Scenes, "Hollow.json"), "{ \"format\": 2 }");
        File.WriteAllText(Path.Combine(temp.Scenes, "notes.txt"), "not a scene");
        File.WriteAllText(Path.Combine(temp.Scenes, "Half.json.tmp"), "{");

        Assert.Equal(["Good"], Folder.SceneNames());
        Assert.Equal(["Garbage.json", "Hollow.json"], temp.Unreadable.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LoadingAnOlderSceneBacksItUpThenRewritesIt()
    {
        WriteFormatOne("Harbour");
        var original = File.ReadAllBytes(temp.ScenePath("Harbour"));

        var scene = Folder.LoadScene("Harbour");

        // The fixture holds five tracks.
        Assert.Equal(5, scene.Tracks.Count);
        Assert.Equal(2, SceneJson.FormatOf(File.ReadAllText(temp.ScenePath("Harbour"))));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Backups, "Harbour.json")));
        Assert.Empty(temp.NotUpgraded);
    }

    [Fact]
    public void LoadingASceneInTheCurrentFormatLeavesItAndMakesNoBackup()
    {
        WriteCompact("Dusk");
        var original = File.ReadAllBytes(temp.ScenePath("Dusk"));

        Folder.LoadScene("Dusk");

        Assert.Equal(original, File.ReadAllBytes(temp.ScenePath("Dusk")));
        Assert.False(Directory.Exists(Folder.BackupsDir));
    }

    [Fact]
    public void AnOlderSceneIsBackedUpOnceWhileItsRewriteKeepsFailing()
    {
        WriteFormatOne("Harbour");
        temp.BlockSaving("Harbour");

        Folder.LoadScene("Harbour");
        now = now.AddSeconds(1);
        var refused = Record.Exception(() => Folder.SaveScene("Harbour", Named("Crane")));

        Assert.True(SceneFolder.IsFileError(refused));
        Assert.Equal(["Harbour.json"], temp.NotUpgraded);
        // Only the first attempt's folder, 12:08:49; the save a second later made none.
        Assert.Equal(["v1-2026-09-29_12-08-49"], BackupFolders());
    }

    [Fact]
    public void AnOlderSceneIsBackedUpOnceWhateverTheCaseOfItsName()
    {
        WriteFormatOne("Harbour");
        temp.BlockSaving("Harbour");

        Folder.LoadScene("harbour");
        now = now.AddSeconds(1);
        Record.Exception(() => Folder.SaveScene("HARBOUR", Named("Crane")));

        // Only the load's folder, 12:08:49; the save a second later, under another case, made none.
        Assert.Equal(["v1-2026-09-29_12-08-49"], BackupFolders());
    }

    [Fact]
    public void AnOlderFilePutBackUnderTheSameNameIsBackedUpAgain()
    {
        WriteFormatOne("Harbour");
        Folder.LoadScene("Harbour");
        now = now.AddSeconds(1);
        WriteFormatOne("Harbour");

        Folder.LoadScene("Harbour");

        // One backup folder per load: 12:08:49, then 12:08:50.
        Assert.Equal(["v1-2026-09-29_12-08-49", "v1-2026-09-29_12-08-50"], BackupFolders());
    }

    [Fact]
    public void AnOlderFileAddedAfterDeletingOneStillStuckIsBackedUp()
    {
        WriteFormatOne("Harbour");
        temp.BlockSaving("Harbour");
        Folder.LoadScene("Harbour");
        Folder.DeleteScene("Harbour");
        Directory.Delete(temp.ScenePath("Harbour") + SceneFolder.TempSuffix);
        now = now.AddSeconds(1);
        WriteFormatOne("Harbour");

        Folder.LoadScene("Harbour");

        // The stuck file's folder at 12:08:49, then the new file's at 12:08:50.
        Assert.Equal(["v1-2026-09-29_12-08-49", "v1-2026-09-29_12-08-50"], BackupFolders());
    }

    [Fact]
    public void AnOlderFileAddedAfterRenamingOneStillStuckIsBackedUp()
    {
        WriteFormatOne("Harbour");
        temp.BlockSaving("Harbour");
        Folder.LoadScene("Harbour");
        Folder.RenameScene("Harbour", "Quay");
        Directory.Delete(temp.ScenePath("Harbour") + SceneFolder.TempSuffix);
        now = now.AddSeconds(1);
        WriteFormatOne("Harbour");

        Folder.LoadScene("Harbour");

        // The stuck file's folder at 12:08:49, then the new file's at 12:08:50.
        Assert.Equal(["v1-2026-09-29_12-08-49", "v1-2026-09-29_12-08-50"], BackupFolders());
    }

    [Fact]
    public void UpgradingAllRewritesEveryOlderSceneIntoOneBackupFolder()
    {
        WriteFormatOne("Harbour");
        WriteFormatOne("Quay");
        WriteCompact("Dusk");
        var current = File.ReadAllBytes(temp.ScenePath("Dusk"));

        Assert.Empty(Folder.UpgradeAll());

        Assert.Equal(2, SceneJson.FormatOf(File.ReadAllText(temp.ScenePath("Harbour"))));
        Assert.Equal(2, SceneJson.FormatOf(File.ReadAllText(temp.ScenePath("Quay"))));
        Assert.Equal(current, File.ReadAllBytes(temp.ScenePath("Dusk")));
        Assert.Equal([Backups], Directory.GetDirectories(Folder.BackupsDir));
        Assert.Equal(
            ["Harbour.json", "Quay.json"],
            Directory.GetFiles(Backups).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void ABackupFolderThatAlreadyExistsIsReused()
    {
        Directory.CreateDirectory(Backups);
        File.WriteAllText(Path.Combine(Backups, "Quay.json"), "earlier");
        WriteFormatOne("Harbour");

        Folder.LoadScene("Harbour");

        Assert.Equal(
            ["Harbour.json", "Quay.json"],
            Directory.GetFiles(Backups).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)
        );
        Assert.Equal("earlier", File.ReadAllText(Path.Combine(Backups, "Quay.json")));
    }

    [Fact]
    public void AnOlderSceneWhoseBackupFailsStillLoadsAndIsLeftAsItIs()
    {
        WriteFormatOne("Harbour");
        var original = File.ReadAllBytes(temp.ScenePath("Harbour"));
        temp.BlockBackups();

        Assert.Equal(5, Folder.LoadScene("Harbour").Tracks.Count);

        Assert.Equal(original, File.ReadAllBytes(temp.ScenePath("Harbour")));
        Assert.Equal(["Harbour.json"], temp.NotUpgraded);
    }

    [Fact]
    public void SavingOverAnOlderSceneWhoseBackupFailsIsRefusedAndLeavesIt()
    {
        WriteFormatOne("Harbour");
        var original = File.ReadAllBytes(temp.ScenePath("Harbour"));
        temp.BlockBackups();

        Assert.ThrowsAny<IOException>(() => Folder.SaveScene("Harbour", Named("Crane")));

        Assert.Equal(original, File.ReadAllBytes(temp.ScenePath("Harbour")));
    }

    [Fact]
    public void UpgradingAllReportsASceneWhoseBackupFailsAndLeavesIt()
    {
        WriteFormatOne("Harbour");
        var original = File.ReadAllBytes(temp.ScenePath("Harbour"));
        temp.BlockBackups();

        var refusal = Assert.Single(Folder.UpgradeAll());

        Assert.StartsWith("Could not upgrade Harbour.json:", refusal);
        Assert.Equal(original, File.ReadAllBytes(temp.ScenePath("Harbour")));
    }

    [Fact]
    public void ANewerSceneIsListedWithNoTrackCountButNotOpenedOrUpgraded()
    {
        Folder.SaveScene("Dusk", Named("Crane"));
        File.WriteAllText(temp.ScenePath("Future"), "{ \"format\": 3 }");

        var future = Assert.Single(Folder.SceneEntries(), e => e.Name == "Future");
        Assert.Null(future.Tracks);
        Assert.Equal(["Dusk"], Folder.SceneNames());
        Assert.Throws<NewerFormatException>(() => Folder.LoadScene("Future"));
        Assert.Empty(Folder.UpgradeAll());
        Assert.Equal("{ \"format\": 3 }", File.ReadAllText(temp.ScenePath("Future")));
        Assert.Empty(temp.Unreadable);
    }

    [Fact]
    public void UpgradingAllLeavesFilesThatCantBeReadAsTheyAre()
    {
        File.WriteAllText(temp.ScenePath("Broken"), "{ \"format\": 1 }");
        File.WriteAllText(temp.ScenePath("Garbage"), "{ not json");

        Assert.Empty(Folder.UpgradeAll());

        Assert.Equal("{ \"format\": 1 }", File.ReadAllText(temp.ScenePath("Broken")));
        Assert.Equal("{ not json", File.ReadAllText(temp.ScenePath("Garbage")));
        Assert.False(Directory.Exists(Folder.BackupsDir));
        Assert.Empty(Folder.SceneEntries());
        Assert.Equal(["Broken.json", "Garbage.json"], temp.Unreadable.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BackupsNeverAppearInTheSceneList()
    {
        WriteFormatOne("Harbour");

        Folder.UpgradeAll();

        Assert.Equal(["Harbour"], Folder.SceneEntries().Select(e => e.Name));
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
        // Literal times, not DateTime.Now: a coarse file clock (as on windows-latest CI) can round
        // a same-second write outside an InRange(before, after) check.
        var duskModified = new DateTime(2024, 1, 2, 3, 4, 5);
        var dawnModified = new DateTime(2024, 5, 6, 7, 8, 9);
        Folder.SaveScene("Dusk", Named("Crane"));
        File.SetLastWriteTime(Path.Combine(temp.Scenes, "Dusk.json"), duskModified);
        Folder.SaveScene("Dawn", DemoScene());
        File.SetLastWriteTime(Path.Combine(temp.Scenes, "Dawn.json"), dawnModified);

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
        Assert.Equal(dawnModified, dawn.Modified);
        Assert.Equal(duskModified, dusk.Modified);
    }

    [Fact]
    public void PresetEntriesHaveNoTrackCount()
    {
        var modified = new DateTime(2024, 3, 4, 5, 6, 7);
        Folder.SavePreset("Orbit", new Preset(TrackEditing.Empty(), 0f));
        File.SetLastWriteTime(Path.Combine(temp.Presets, "Orbit.json"), modified);

        var entry = Assert.Single(Folder.PresetEntries());

        Assert.Equal("Orbit", entry.Name);
        Assert.Null(entry.Tracks);
        Assert.Equal(modified, entry.Modified);
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
