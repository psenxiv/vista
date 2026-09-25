namespace Vista.Core.Scenes;

/// <summary>Scene and preset files in a vistaxiv folder.</summary>
public sealed class SceneFolder
{
    /// <summary>The folder Vista keeps inside the chosen parent.</summary>
    public const string FolderName = "vistaxiv";

    private const string Extension = ".json";

    private readonly Action<string, Exception>? unreadable;

    /// <summary>The folder at <paramref name="root"/>, which is &lt;parent&gt;/vistaxiv, reporting files it can't read to <paramref name="unreadable"/>.</summary>
    public SceneFolder(string root, Action<string, Exception>? unreadable = null)
    {
        Root = root;
        this.unreadable = unreadable;
    }

    /// <summary>The vistaxiv folder inside <paramref name="parent"/>.</summary>
    public static string RootFor(string parent) => Path.Combine(parent, FolderName);

    /// <summary>True for the errors reading or writing a file can be expected to throw: missing, locked or forbidden.</summary>
    public static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException;

    /// <summary>True for a file error, or a file that was read but isn't a scene or preset Vista can use.</summary>
    public static bool IsUnreadable(Exception e) => e is InvalidDataException || IsFileError(e);

    /// <summary>The vistaxiv folder.</summary>
    public string Root { get; }

    /// <summary>The folder scene files are kept in.</summary>
    public string ScenesDir => Path.Combine(Root, "scenes");

    /// <summary>The folder preset files are kept in.</summary>
    public string PresetsDir => Path.Combine(Root, "presets");

    /// <summary>True when the folder and its scenes and presets folders all exist.</summary>
    public bool Exists => Directory.Exists(Root) && Directory.Exists(ScenesDir) && Directory.Exists(PresetsDir);

    /// <summary>Creates whichever of the folder, scenes and presets are missing.</summary>
    public void Create()
    {
        Directory.CreateDirectory(ScenesDir);
        Directory.CreateDirectory(PresetsDir);
    }

    /// <summary>The names of the scene files that can be read, sorted ignoring case.</summary>
    public IReadOnlyList<string> SceneNames() => Names(ScenesDir, json => SceneJson.Read(json));

    /// <summary>The scene in file <paramref name="name"/>.</summary>
    public Scene LoadScene(string name) => SceneJson.Read(File.ReadAllText(PathOf(ScenesDir, name)));

    /// <summary>Writes <paramref name="scene"/> to file <paramref name="name"/>, replacing any there.</summary>
    public void SaveScene(string name, Scene scene) => Write(PathOf(ScenesDir, name), SceneJson.Write(scene));

    /// <summary>Writes <paramref name="json"/> as scene file <paramref name="name"/> unless a scene file has that name, ignoring case; true when written.</summary>
    public bool AddScene(string name, string json)
    {
        if (Vista.Core.Scenes.SceneNames.Taken(name, SceneFiles()))
            return false;
        Write(PathOf(ScenesDir, name), json);
        return true;
    }

    /// <summary>Renames scene file <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void RenameScene(string from, string to)
    {
        var source = PathOf(ScenesDir, from);
        var target = PathOf(ScenesDir, to);
        if (from == to)
            return;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            // A case-insensitive file system sees a case-only rename as a move onto itself.
            var step = Path.Combine(ScenesDir, $"{Guid.NewGuid():N}.tmp");
            File.Move(source, step);
            source = step;
        }

        File.Move(source, target);
    }

    /// <summary>Deletes scene file <paramref name="name"/>.</summary>
    public void DeleteScene(string name) => File.Delete(PathOf(ScenesDir, name));

    /// <summary>The names of the preset files that can be read, sorted ignoring case.</summary>
    public IReadOnlyList<string> PresetNames() => Names(PresetsDir, json => SceneJson.ReadPreset(json));

    /// <summary>The preset in file <paramref name="name"/>, its track named <paramref name="name"/>.</summary>
    public Preset LoadPreset(string name)
    {
        var preset = SceneJson.ReadPreset(File.ReadAllText(PathOf(PresetsDir, name)));
        return preset with { Track = preset.Track with { Name = name } };
    }

    /// <summary>Writes <paramref name="preset"/> to file <paramref name="name"/>, replacing any there.</summary>
    public void SavePreset(string name, Preset preset) =>
        Write(PathOf(PresetsDir, name), SceneJson.WritePreset(preset));

    /// <summary>Deletes preset file <paramref name="name"/>.</summary>
    public void DeletePreset(string name) => File.Delete(PathOf(PresetsDir, name));

    /// <summary>The names of all scene files, readable or not.</summary>
    internal IReadOnlyList<string> SceneFiles() =>
        Files(ScenesDir).Select(f => Path.GetFileNameWithoutExtension(f)).ToList();

    private static string PathOf(string dir, string name) => Path.Combine(dir, name + Extension);

    private static void Write(string path, string json)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private IEnumerable<string> Files(string dir)
    {
        try
        {
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
            return files.Where(f => string.Equals(Path.GetExtension(f), Extension, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (IsFileError(e))
        {
            unreadable?.Invoke(dir, e);
            return [];
        }
    }

    private List<string> Names(string dir, Action<string> read)
    {
        var names = new List<string>();
        foreach (var file in Files(dir))
        {
            try
            {
                read(File.ReadAllText(file));
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception e) when (IsUnreadable(e))
            {
                unreadable?.Invoke(file, e);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }
}
