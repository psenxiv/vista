namespace Vista.Core.Scenes;

/// <summary>The open scene's name and file: opening, switching, new, rename, duplicate, delete and debounced saving.</summary>
public sealed class SceneLibrary
{
    private const string Stem = "Scene";
    private const string Exists = "A scene with that name exists";

    private readonly Func<Scene> scene;
    private readonly Func<Scene, string?> load;
    private SaveDebounce? debounce;
    private Scene? saved;

    /// <summary>Keeps <paramref name="folder"/>'s scenes, reading the session's scene from <paramref name="scene"/> and loading through <paramref name="load"/>.</summary>
    public SceneLibrary(SceneFolder folder, Func<Scene> scene, Func<Scene, string?> load)
    {
        Folder = folder;
        this.scene = scene;
        this.load = load;
    }

    /// <summary>The folder the scenes are in.</summary>
    public SceneFolder Folder { get; }

    /// <summary>The open scene's name, or empty until one is open.</summary>
    public string CurrentName { get; private set; } = string.Empty;

    /// <summary>The names of the readable scenes, sorted ignoring case.</summary>
    public IReadOnlyList<string> Scenes() => Folder.SceneNames();

    /// <summary>Opens <paramref name="last"/> if it exists, else the first scene by name, else a new Scene N. Returns why it was refused, or null.</summary>
    public string? Open(string? last)
    {
        var names = Scenes();
        var name =
            names.FirstOrDefault(n => string.Equals(n, last, StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault();
        return name is null ? Create(SceneNames.NextFree(Stem, Folder.SceneFiles())) : Load(name);
    }

    /// <summary>Saves the open scene and loads <paramref name="name"/>. Returns why it was refused, or null.</summary>
    public string? Switch(string name) => SaveNow() ?? Load(name);

    /// <summary>Saves the open scene and opens a new empty one called <paramref name="name"/>. Returns why it was refused, or null.</summary>
    public string? New(string name)
    {
        var trimmed = name.Trim();
        return NameRefusal(trimmed) ?? SaveNow() ?? Create(trimmed);
    }

    /// <summary>Why <paramref name="name"/> can't name a new scene, or with <paramref name="renaming"/> the open one; every scene file counts as taken, readable or not.</summary>
    public string? NameRefusal(string name, bool renaming = false)
    {
        var trimmed = name.Trim();
        var same = renaming && string.Equals(trimmed, CurrentName, StringComparison.OrdinalIgnoreCase);
        return SceneNames.Refusal(trimmed) ?? (!same && SceneNames.Taken(trimmed, Folder.SceneFiles()) ? Exists : null);
    }

    /// <summary>Renames the open scene's file to <paramref name="name"/>, keeping undo history. Returns why it was refused, or null.</summary>
    public string? Rename(string name)
    {
        var trimmed = name.Trim();
        var refusal = NameRefusal(trimmed, renaming: true);
        if (refusal is not null)
            return refusal;
        if (trimmed == CurrentName)
            return null;

        try
        {
            Folder.RenameScene(CurrentName, trimmed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {trimmed}: {e.Message}";
        }

        CurrentName = trimmed;
        return null;
    }

    /// <summary>Saves the open scene, saves a copy called <paramref name="name"/> and opens the copy. Returns why it was refused, or null.</summary>
    public string? Duplicate(string name)
    {
        var trimmed = name.Trim();
        return NameRefusal(trimmed) ?? SaveNow() ?? Save(trimmed, scene()) ?? Adopt(trimmed, scene());
    }

    /// <summary>Deletes the open scene's file and opens the first remaining scene, or a new Scene N. Returns why it was refused, or null.</summary>
    public string? Delete()
    {
        try
        {
            Folder.DeleteScene(CurrentName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {CurrentName}: {e.Message}";
        }

        CurrentName = string.Empty;
        debounce = null;
        saved = null;
        return Open(null);
    }

    /// <summary>Creates the folder again after it has gone and writes the open scene into it, keeping undo history. Returns why it was refused, or null.</summary>
    public string? Recreate()
    {
        try
        {
            Folder.Create();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {CurrentName}: {e.Message}";
        }

        return CurrentName.Length == 0 ? null : Save(CurrentName, scene());
    }

    /// <summary>Saves the open scene if it has changed since it was last saved. Returns why it was refused, or null.</summary>
    public string? SaveNow()
    {
        if (CurrentName.Length == 0)
            return null;
        var current = scene();
        return ReferenceEquals(current, saved) ? null : Save(CurrentName, current);
    }

    /// <summary>Saves the open scene once it has been unchanged for SaveDebounce.DelaySeconds after a change, at time <paramref name="now"/> in seconds. Returns why it was refused, or null.</summary>
    public string? Tick(double now) => debounce is not null && debounce.Due(scene(), now) ? SaveNow() : null;

    private string? Create(string name)
    {
        var fresh = SceneEditing.New();
        return Save(name, fresh) ?? Adopt(name, fresh);
    }

    private string? Load(string name)
    {
        Scene read;
        try
        {
            read = Folder.LoadScene(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Could not open {name}: {e.Message}";
        }

        return Adopt(name, read);
    }

    private string? Adopt(string name, Scene opened)
    {
        var refusal = load(opened);
        if (refusal is not null)
            return refusal;
        CurrentName = name;
        saved = scene();
        debounce = new SaveDebounce(saved);
        return null;
    }

    private string? Save(string name, Scene toSave)
    {
        try
        {
            Folder.SaveScene(name, toSave);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {name}: {e.Message}";
        }

        if (name == CurrentName)
        {
            saved = toSave;
            debounce?.Saved(toSave);
        }

        return null;
    }
}
