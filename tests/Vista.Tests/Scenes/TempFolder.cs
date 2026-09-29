using Vista.Core.Scenes;

namespace Vista.Tests.Scenes;

/// <summary>A created vistaxiv folder in a fresh temporary directory, deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly string parent = Directory.CreateTempSubdirectory("vista-tests-").FullName;

    /// <summary>A folder whose backups are named for <paramref name="clock"/>, the local time by default.</summary>
    internal TempFolder(Func<DateTime>? clock = null)
    {
        Folder = new SceneFolder(
            SceneFolder.RootFor(parent),
            (path, e) => Unreadable.Add(Path.GetFileName(path)),
            (path, e) => NotUpgraded.Add(Path.GetFileName(path)),
            clock
        );
        Folder.Create();
    }

    internal SceneFolder Folder { get; }

    /// <summary>The parent directory vistaxiv was created in.</summary>
    internal string Parent => parent;

    /// <summary>The file names reported unreadable, in the order reported.</summary>
    internal List<string> Unreadable { get; } = [];

    /// <summary>The file names of older scenes opened without being backed up and rewritten, in the order reported.</summary>
    internal List<string> NotUpgraded { get; } = [];

    internal string Scenes => Folder.ScenesDir;

    internal string Presets => Folder.PresetsDir;

    /// <summary>The file names in scenes/, sorted ordinally.</summary>
    internal string[] SceneFiles() =>
        Directory.GetFiles(Scenes).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();

    public void Dispose() => Directory.Delete(parent, recursive: true);
}
