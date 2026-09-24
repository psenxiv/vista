using Vista.Core.Scenes;

namespace Vista.Tests.Scenes;

/// <summary>A created vistaxiv folder in a fresh temporary directory, deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly string parent = Directory.CreateTempSubdirectory("vista-tests-").FullName;

    internal TempFolder()
    {
        Folder = new SceneFolder(SceneFolder.RootFor(parent), (path, e) => Unreadable.Add(Path.GetFileName(path)));
        Folder.Create();
    }

    internal SceneFolder Folder { get; }

    /// <summary>The file names reported unreadable, in the order reported.</summary>
    internal List<string> Unreadable { get; } = [];

    internal string Scenes => Folder.ScenesDir;

    internal string Presets => Folder.PresetsDir;

    /// <summary>The file names in scenes/, sorted ordinally.</summary>
    internal string[] SceneFiles() =>
        Directory.GetFiles(Scenes).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();

    public void Dispose() => Directory.Delete(parent, recursive: true);
}

/// <summary>Scenes told apart by their first track's name.</summary>
internal static class SceneFixtures
{
    /// <summary>A new scene whose one track is called <paramref name="trackName"/>.</summary>
    internal static Scene Named(string trackName)
    {
        var scene = SceneEditing.New();
        return SceneEditing.Rename(scene, scene.Tracks[0].Id, trackName);
    }

    /// <summary>The name of the first track in the scene file <paramref name="name"/>.</summary>
    internal static string FirstTrackIn(TempFolder temp, string name) => temp.Folder.LoadScene(name).Tracks[0].Name;
}
