using System.Globalization;

namespace Vista.Core.Scenes;

/// <summary>Scene and preset files in a vistaxiv folder.</summary>
public sealed class SceneFolder
{
    /// <summary>The folder Vista keeps inside the chosen parent.</summary>
    public const string FolderName = "vistaxiv";

    /// <summary>What a save adds to a file's name for the temporary copy it writes before moving it into place.</summary>
    public const string TempSuffix = ".tmp";

    private const string Extension = ".json";

    private readonly Action<string, Exception>? unreadable;
    private readonly Action<string, Exception>? notUpgraded;
    private readonly Func<DateTime> clock;
    private readonly HashSet<string> backedUp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder at <paramref name="root"/>, which is &lt;parent&gt;/vistaxiv, reporting files it can't read to <paramref name="unreadable"/> and older scenes it opened but couldn't back up and rewrite to <paramref name="notUpgraded"/>; <paramref name="clock"/> names backup folders, the local time by default.</summary>
    public SceneFolder(
        string root,
        Action<string, Exception>? unreadable = null,
        Action<string, Exception>? notUpgraded = null,
        Func<DateTime>? clock = null
    )
    {
        Root = root;
        this.unreadable = unreadable;
        this.notUpgraded = notUpgraded;
        this.clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>The vistaxiv folder inside <paramref name="parent"/>.</summary>
    public static string RootFor(string parent) => Path.Combine(parent, FolderName);

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> name the same folder, ignoring case and a trailing separator as Windows does.</summary>
    public static bool SameParent(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>True for the errors reading or writing a file can be expected to throw: missing, locked or forbidden.</summary>
    public static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException;

    /// <summary>True for a file error, or a file that was read but isn't a scene or preset this Vista can use.</summary>
    public static bool IsUnreadable(Exception e) => e is InvalidDataException or NewerFormatException || IsFileError(e);

    /// <summary>The vistaxiv folder.</summary>
    public string Root { get; }

    /// <summary>The folder scene files are kept in.</summary>
    public string ScenesDir => Path.Combine(Root, "scenes");

    /// <summary>The folder preset files are kept in.</summary>
    public string PresetsDir => Path.Combine(Root, "presets");

    /// <summary>The folder older scene files are copied into before they are rewritten in the current format.</summary>
    public string BackupsDir => Path.Combine(Root, "backups");

    /// <summary>True when the folder and its scenes and presets folders all exist.</summary>
    public bool Exists => Directory.Exists(Root) && Directory.Exists(ScenesDir) && Directory.Exists(PresetsDir);

    /// <summary>Creates whichever of the folder, scenes and presets are missing.</summary>
    public void Create()
    {
        Directory.CreateDirectory(ScenesDir);
        Directory.CreateDirectory(PresetsDir);
    }

    /// <summary>The scene name of <paramref name="path"/> when it is a scene file in the scenes folder, else null.</summary>
    public string? SceneNameOf(string path) =>
        Path.GetDirectoryName(path) is { Length: > 0 } dir
        && SameParent(dir, ScenesDir)
        && string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(path)
            : null;

    /// <summary>The scene files that can be read, with their track count, and those saved by a newer Vista, with none; sorted by name ignoring case.</summary>
    public IReadOnlyList<FileEntry> SceneEntries() =>
        Entries(
            ScenesDir,
            json =>
            {
                try
                {
                    return SceneJson.Read(json).Tracks.Count;
                }
                catch (NewerFormatException)
                {
                    return null;
                }
            }
        );

    /// <summary>The scene in file <paramref name="name"/>; a file in an earlier format is backed up and rewritten in the current one first, or only read, and reported, when that fails.</summary>
    public Scene LoadScene(string name)
    {
        var path = PathOf(ScenesDir, name);
        var json = File.ReadAllText(path);
        var scene = SceneJson.Read(json);
        if (SceneJson.FormatOf(json) < SceneJson.SceneFormat)
        {
            try
            {
                WriteScene(path, scene, clock());
            }
            catch (Exception e) when (IsFileError(e))
            {
                notUpgraded?.Invoke(path, e);
            }
        }

        return scene;
    }

    /// <summary>Writes <paramref name="scene"/> to file <paramref name="name"/>, replacing any there; a file there in an earlier format is backed up first, and left as it is when that fails.</summary>
    public void SaveScene(string name, Scene scene) => WriteScene(PathOf(ScenesDir, name), scene, clock());

    /// <summary>Backs up and rewrites every scene file in an earlier format, all into one backup folder; files that can't be read are left as they are. Returns why any other file couldn't be upgraded.</summary>
    public IReadOnlyList<string> UpgradeAll()
    {
        var now = clock();
        var refusals = new List<string>();
        foreach (var path in Files(ScenesDir))
        {
            try
            {
                var json = File.ReadAllText(path);
                if (FormatIn(json) < SceneJson.SceneFormat && TryRead(json) is { } scene)
                    WriteScene(path, scene, now);
            }
            catch (Exception e) when (IsFileError(e))
            {
                refusals.Add($"Could not upgrade {Path.GetFileName(path)}: {e.Message}");
            }
        }

        return refusals;
    }

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
            var step = Path.Combine(ScenesDir, $"{Guid.NewGuid():N}{TempSuffix}");
            File.Move(source, step);
            source = step;
        }

        File.Move(source, target);
        backedUp.Remove(PathOf(ScenesDir, from));
    }

    /// <summary>Deletes scene file <paramref name="name"/>.</summary>
    public void DeleteScene(string name)
    {
        var path = PathOf(ScenesDir, name);
        File.Delete(path);
        backedUp.Remove(path);
    }

    /// <summary>The names of the preset files that can be read, sorted ignoring case.</summary>
    public IReadOnlyList<string> PresetNames() => PresetEntries().Select(e => e.Name).ToList();

    /// <summary>The preset files that can be read, sorted by name ignoring case; each entry's <see cref="FileEntry.Tracks"/> is null.</summary>
    public IReadOnlyList<FileEntry> PresetEntries() =>
        Entries(
            PresetsDir,
            json =>
            {
                SceneJson.ReadPreset(json);
                return null;
            }
        );

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

    private static Scene? TryRead(string json)
    {
        try
        {
            return SceneJson.Read(json);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="scene"/> to <paramref name="path"/>, first copying a file there in an earlier format into a backup folder named for its format and <paramref name="now"/>, once until the rewrite succeeds; throws, writing nothing, when the copy fails.</summary>
    private void WriteScene(string path, Scene scene, DateTime now)
    {
        if (!backedUp.Contains(path) && FormatOnDisk(path) is { } format && format < SceneJson.SceneFormat)
        {
            BackUp(path, format, now);
            backedUp.Add(path);
        }

        Write(path, SceneJson.Write(scene));
        backedUp.Remove(path);
    }

    /// <summary>Copies the file at <paramref name="path"/>, in format <paramref name="format"/>, into the backup folder for that format and <paramref name="now"/>, removing the folder again if it made it and the copy fails.</summary>
    private void BackUp(string path, int format, DateTime now)
    {
        var backups = Path.Combine(
            BackupsDir,
            $"v{format}-{now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}"
        );
        var made = !Directory.Exists(backups);
        Directory.CreateDirectory(backups);
        try
        {
            File.Copy(path, Path.Combine(backups, Path.GetFileName(path)));
        }
        catch (Exception e) when (made && IsFileError(e))
        {
            Directory.Delete(backups);
            throw;
        }
    }

    /// <summary>The format of the file at <paramref name="path"/>, or null when there is none or it has no format number.</summary>
    private static int? FormatOnDisk(string path) => File.Exists(path) ? FormatIn(File.ReadAllText(path)) : null;

    /// <summary>The format number in <paramref name="json"/>, or null when it has none.</summary>
    private static int? FormatIn(string json)
    {
        try
        {
            return SceneJson.FormatOf(json);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void Write(string path, string json)
    {
        var temp = path + TempSuffix;
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

    private List<FileEntry> Entries(string dir, Func<string, int?> read)
    {
        var entries = new List<FileEntry>();
        foreach (var file in Files(dir))
        {
            try
            {
                var tracks = read(File.ReadAllText(file));
                entries.Add(new FileEntry(Path.GetFileNameWithoutExtension(file), File.GetLastWriteTime(file), tracks));
            }
            catch (Exception e) when (IsUnreadable(e))
            {
                unreadable?.Invoke(file, e);
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }
}
