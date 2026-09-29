namespace Vista.Core.Scenes;

/// <summary>The open scene's name and file: opening, switching, new, rename, duplicate, delete and debounced saving.</summary>
public sealed class SceneLibrary
{
    private const string Stem = "Scene";
    private const string Exists = "A scene with that name exists.";

    private readonly Func<Scene> scene;
    private readonly Func<Scene, string?> load;
    private readonly UnreadableNotices notices;
    private SaveDebounce? debounce;
    private Scene? saved;

    /// <summary>Keeps <paramref name="folder"/>'s scenes, reading the session's scene from <paramref name="scene"/> and loading through <paramref name="load"/>; <paramref name="notices"/> is shared by libraries that tell the same player.</summary>
    public SceneLibrary(SceneFolder folder, Func<Scene> scene, Func<Scene, string?> load, UnreadableNotices notices)
    {
        Folder = folder;
        this.scene = scene;
        this.load = load;
        this.notices = notices;
    }

    /// <summary>The folder the scenes are in.</summary>
    public SceneFolder Folder { get; }

    /// <summary>The open scene's name, or empty until one is open.</summary>
    public string CurrentName { get; private set; } = string.Empty;

    /// <summary>What the last <see cref="Open"/> has to tell the player: its last scene's file can't be read, so another scene opened; else null.</summary>
    public string? Notice { get; private set; }

    /// <summary>The name "New scene" suggests: the first free "Scene N" among every scene file, readable or not.</summary>
    public string NewSuggestion() => SceneNames.NextFree(Stem, Folder.SceneFiles());

    /// <summary>The name "Duplicate scene" suggests for <paramref name="name"/>: its first free copy name among every scene file, readable or not.</summary>
    public string CopySuggestion(string name) => SceneNames.CopyOf(name, Folder.SceneFiles());

    /// <summary>The name "Duplicate scene" suggests: the open scene's first free copy name among every scene file, readable or not.</summary>
    public string CopySuggestion() => CopySuggestion(CurrentName);

    /// <summary>Opens <paramref name="last"/> if it exists, else the first scene by name, else a new Scene N; sets <see cref="Notice"/> when <paramref name="last"/> is a file that can't be read. Returns why it was refused, or null.</summary>
    public string? Open(string? last)
    {
        Notice = null;
        var entries = Folder.SceneEntries();
        var names = FileEntry.OpenableNames(entries);
        var name =
            names.FirstOrDefault(n => string.Equals(n, last, StringComparison.OrdinalIgnoreCase))
            ?? (names.Count > 0 ? names[0] : null);
        var refusal = name is null ? Create(NewSuggestion()) : Load(name);
        // A file in the folder that isn't listed at all, not even as one from a newer Vista, can't be read.
        if (
            refusal is null
            && last is not null
            && !entries.Any(e => string.Equals(e.Name, last, StringComparison.OrdinalIgnoreCase))
            && Folder.SceneFiles().FirstOrDefault(f => string.Equals(f, last, StringComparison.OrdinalIgnoreCase))
                is { } unreadable
        )
            Notice = notices.Replaced(unreadable, CurrentName);
        return refusal;
    }

    /// <summary>Saves the open scene and loads <paramref name="name"/>; already open (ignoring case), does neither. Returns why it was refused, or null.</summary>
    public string? Switch(string name) =>
        string.Equals(name, CurrentName, StringComparison.OrdinalIgnoreCase) ? null : SaveNow() ?? Load(name);

    /// <summary>Saves the open scene and opens a new empty one called <paramref name="name"/>. Returns why it was refused, or null.</summary>
    public string? New(string name)
    {
        var trimmed = name.Trim();
        return NameRefusal(trimmed) ?? SaveNow() ?? Create(trimmed);
    }

    /// <summary>Why <paramref name="name"/> can't name a scene, or with <paramref name="renaming"/> that name (case-only changes included); every scene file counts as taken, readable or not.</summary>
    public string? NameRefusal(string name, string? renaming = null)
    {
        var trimmed = name.Trim();
        var same = renaming is not null && string.Equals(trimmed, renaming, StringComparison.OrdinalIgnoreCase);
        return SceneNames.Refusal(trimmed) ?? (!same && SceneNames.Taken(trimmed, Folder.SceneFiles()) ? Exists : null);
    }

    /// <summary>Renames the open scene's file to <paramref name="name"/>, keeping undo history. Returns why it was refused, or null.</summary>
    public string? Rename(string name) => Rename(CurrentName, name);

    /// <summary>Renames scene file <paramref name="from"/> to <paramref name="to"/>; when <paramref name="from"/> is the open scene, also updates its open name. Returns why it was refused, or null.</summary>
    public string? Rename(string from, string to)
    {
        var trimmed = to.Trim();
        var refusal = NameRefusal(trimmed, renaming: from);
        if (refusal is not null)
            return refusal;
        if (trimmed == from)
            return null;

        try
        {
            Folder.RenameScene(from, trimmed);
        }
        catch (Exception e) when (SceneFolder.IsFileError(e))
        {
            return $"Could not save {trimmed}: {e.Message}";
        }

        if (from == CurrentName)
            CurrentName = trimmed;
        return null;
    }

    /// <summary>Saves the open scene, saves a copy called <paramref name="name"/> and opens the copy. Returns why it was refused, or null.</summary>
    public string? Duplicate(string name) => Duplicate(CurrentName, name);

    /// <summary>Copies scene file <paramref name="from"/> to <paramref name="to"/>; when <paramref name="from"/> is the open scene, saves it first and opens the copy, otherwise the open scene is untouched. Returns why it was refused, or null.</summary>
    public string? Duplicate(string from, string to)
    {
        var trimmed = to.Trim();
        if (from == CurrentName)
            return NameRefusal(trimmed) ?? SaveNow() ?? Save(trimmed, scene()) ?? Adopt(trimmed, scene());

        var refusal = NameRefusal(trimmed);
        if (refusal is not null)
            return refusal;

        Scene source;
        try
        {
            source = Folder.LoadScene(from);
        }
        catch (Exception e) when (SceneFolder.IsUnreadable(e))
        {
            return $"Could not open {from}: {e.Message}";
        }

        return Save(trimmed, source);
    }

    /// <summary>Deletes the open scene's file and opens the first remaining scene, or a new Scene N. Returns why it was refused, or null.</summary>
    public string? Delete() => Delete(CurrentName);

    /// <summary>Deletes scene file <paramref name="name"/>; when it's the open scene, also opens the first remaining scene, or a new Scene N. Returns why it was refused, or null.</summary>
    public string? Delete(string name)
    {
        try
        {
            Folder.DeleteScene(name);
        }
        catch (Exception e) when (SceneFolder.IsFileError(e))
        {
            return $"Could not delete {name}: {e.Message}";
        }

        if (name != CurrentName)
            return null;

        CurrentName = string.Empty;
        debounce = null;
        saved = null;
        return Open(null);
    }

    /// <summary>What choosing <paramref name="chosen"/> does, with <paramref name="current"/> chosen before (null for none), <paramref name="ready"/> when its folder is open and there, and <paramref name="sceneOpen"/> when a scene is open in it.</summary>
    public static FolderChange Change(string? current, string chosen, bool ready, bool sceneOpen) =>
        current is null ? FolderChange.Reopen
        : !SceneFolder.SameParent(current, chosen) ? FolderChange.Move
        : ready ? FolderChange.Keep
        : sceneOpen ? FolderChange.Recreate
        : FolderChange.Reopen;

    /// <summary>Leaves this folder for <paramref name="next"/>'s: saves the open scene here and opens <paramref name="next"/>'s first scene, or carries the scene into <paramref name="next"/> when saving here is refused. Not moved only when neither save works, and then nothing changed.</summary>
    public (bool Moved, string? Refusal) MoveTo(SceneLibrary next)
    {
        // Written even when unchanged, since the file may have gone with the folder.
        if (CurrentName.Length == 0 || Save(CurrentName, scene()) is null)
            return (true, next.Open(null));
        var refusal = next.Carry(CurrentName, scene());
        return (refusal is null, refusal);
    }

    /// <summary>Writes <paramref name="carried"/>, already open in the session, as <paramref name="name"/> or its copy name if that's taken, and keeps it open without reloading, so undo survives.</summary>
    private string? Carry(string name, Scene carried)
    {
        var free = SceneNames.Taken(name, Folder.SceneFiles()) ? CopySuggestion(name) : name;
        var refusal = Save(free, carried);
        if (refusal is not null)
            return refusal;
        CurrentName = free;
        saved = carried;
        debounce = new SaveDebounce(carried);
        return null;
    }

    /// <summary>Creates the folder again after it has gone and writes the open scene into it, keeping undo history. Returns why it was refused, or null.</summary>
    public string? Recreate()
    {
        try
        {
            Folder.Create();
        }
        catch (Exception e) when (SceneFolder.IsFileError(e))
        {
            return $"Could not create the save folder: {e.Message}";
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
        catch (Exception e) when (SceneFolder.IsUnreadable(e))
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
        catch (Exception e) when (SceneFolder.IsFileError(e))
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
