using System.Diagnostics;
using Vista.Core.Scenes;
using Vista.Core.Session;

namespace Vista.Plugin.Session;

/// <summary>The save folder and the open scene's library, kept in step with the settings; asks for Setup when the folder is gone.</summary>
internal sealed class SceneFiles
{
    /// <summary>Why a scene or preset action is refused before Setup has chosen a folder.</summary>
    private const string NoFolder = "No save folder is chosen.";

    private const string DemoResource = "Vista.Demo.";

    private readonly Configuration config;
    private readonly GameSession game;
    private readonly SessionState session;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private SceneLibrary? library;
    private string? tickRefusal;

    public SceneFiles(Configuration config, GameSession game)
    {
        this.config = config;
        this.game = game;
        session = game.State;
        if (config.SaveFolder is { } parent && Directory.Exists(SceneFolder.RootFor(parent)))
            Logged(Use(parent, move: false, config.LastScene));
    }

    /// <summary>Raised when the folder is missing and Setup should be shown.</summary>
    public event EventHandler? SetupNeeded;

    /// <summary>Raised after a scene or preset is written, renamed, duplicated, deleted or added, or the save folder changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The folder chosen before, or null; where the folder picker starts.</summary>
    public string? Chosen => config.SaveFolder;

    /// <summary>True when a folder was chosen but its vistaxiv folder has gone.</summary>
    public bool Lost => config.SaveFolder is not null && !Ready;

    /// <summary>True when a folder is chosen and its vistaxiv folder is there.</summary>
    public bool Ready => library is { } l && l.Folder.Exists;

    /// <summary>The open scene's name, or empty before one is open.</summary>
    public string CurrentName => library?.CurrentName ?? string.Empty;

    /// <summary>The scene files the picker lists, with their track count, read now.</summary>
    public IReadOnlyList<FileEntry> SceneEntries() => library?.Folder.SceneEntries() ?? [];

    /// <summary>Why <paramref name="name"/> can't name a scene, or with <paramref name="renaming"/> that name, or null.</summary>
    public string? NameRefusal(string name, string? renaming = null) =>
        library?.NameRefusal(name, renaming) ?? SceneNames.Refusal(name);

    /// <summary>The name "New scene" suggests, read now; empty with no folder.</summary>
    public string NewSuggestion() => library?.NewSuggestion() ?? string.Empty;

    /// <summary>The name "Duplicate scene" suggests, read now; empty with no folder.</summary>
    public string CopySuggestion() => library?.CopySuggestion() ?? string.Empty;

    /// <summary>The name "Duplicate scene" suggests for <paramref name="name"/>, read now; empty with no folder.</summary>
    public string CopySuggestion(string name) => library?.CopySuggestion(name) ?? string.Empty;

    /// <summary>The preset names in the folder, read now.</summary>
    public IReadOnlyList<string> PresetNames() => library?.Folder.PresetNames() ?? [];

    /// <summary>The preset files the picker lists, read now.</summary>
    public IReadOnlyList<FileEntry> PresetEntries() => library?.Folder.PresetEntries() ?? [];

    /// <summary>Uses <paramref name="parent"/>'s vistaxiv folder, creating it; keeps, recreates or reopens the current one, or moves the open scene into a different one.</summary>
    public string? Choose(string parent)
    {
        var sceneOpen = library is { } l && l.CurrentName.Length > 0;
        switch (SceneLibrary.Change(config.SaveFolder, parent, Ready, sceneOpen))
        {
            case FolderChange.Keep:
                return null;
            case FolderChange.Recreate when library is { } lost:
            {
                var refusal = Checked(lost.Recreate());
                if (refusal is null)
                    AddDemo(lost.Folder);
                return refusal;
            }
            case FolderChange.Reopen:
                return Use(parent, move: false, config.LastScene);
            case FolderChange.Move:
                return Use(parent, move: true, null);
            default:
                throw new UnreachableException();
        }
    }

    public string? Switch(string name) => Run(l => l.Switch(name));

    public string? New(string name) => Run(l => l.New(name));

    public string? Rename(string name) => Run(l => l.Rename(name));

    /// <summary>Renames scene file <paramref name="from"/> to <paramref name="to"/>; when <paramref name="from"/> is the open scene, also updates its open name.</summary>
    public string? Rename(string from, string to) => Run(l => l.Rename(from, to));

    public string? Duplicate(string name) => Run(l => l.Duplicate(name));

    /// <summary>Copies scene file <paramref name="from"/> to <paramref name="to"/>; when <paramref name="from"/> is the open scene, saves it first and opens the copy.</summary>
    public string? Duplicate(string from, string to) => Run(l => l.Duplicate(from, to));

    public string? Delete() => Run(l => l.Delete());

    /// <summary>Deletes scene file <paramref name="name"/>; when it's the open scene, also opens the first remaining scene, or a new one.</summary>
    public string? Delete(string name) => Run(l => l.Delete(name));

    /// <summary>Saves the open scene now if it has changed.</summary>
    public string? SaveNow() => Run(l => l.SaveNow());

    /// <summary>Saves the open scene as the plugin unloads, logging a refusal, since nobody is left to show it.</summary>
    public void SaveBeforeUnload()
    {
        if (library?.SaveNow() is { } refusal)
            Plugin.Log.Warning("[scenes] {Refusal}", refusal);
    }

    /// <summary>Saves the open scene once it has been still long enough. Call once a frame; a failing save retries each frame but reports once.</summary>
    public void Tick()
    {
        if (library is null)
            return;
        var refusal = library.Tick(clock.Elapsed.TotalSeconds);
        if (refusal != tickRefusal && refusal is not null)
            Logged(refusal);
        tickRefusal = refusal;
    }

    /// <summary>Saves track <paramref name="trackId"/> as the preset <paramref name="name"/>, replacing one of that name.</summary>
    public string? SavePreset(string name, Guid trackId) =>
        Files(
            $"save preset {name.Trim()}",
            folder => folder.SavePreset(name.Trim(), Presets.From(session.Scene, trackId))
        );

    /// <summary>Adds the preset <paramref name="name"/> to the scene under the camera.</summary>
    public string? AddPreset(string name)
    {
        if (library is not { } l)
            return NoFolder;
        Preset preset;
        try
        {
            preset = l.Folder.LoadPreset(name);
        }
        catch (Exception e) when (SceneFolder.IsUnreadable(e))
        {
            return Checked($"Could not add preset {name}: {e.Message}");
        }
        return game.AddPreset(preset);
    }

    public string? DeletePreset(string name) => Files($"delete preset {name}", folder => folder.DeletePreset(name));

    /// <summary>Opens the scenes folder, or the presets folder, in the system's file browser, as Dalamud's installer opens folders.</summary>
    public void OpenFolder(bool presets)
    {
        if (library is { } l)
            Dalamud.Utility.Util.OpenLink(presets ? l.Folder.PresetsDir : l.Folder.ScenesDir);
    }

    /// <summary>Creates <paramref name="parent"/>'s vistaxiv folder and uses it: for a move, leaves the current library for it; otherwise opens <paramref name="last"/>. A refused move leaves <see cref="library"/> and the configuration untouched.</summary>
    private string? Use(string parent, bool move, string? last)
    {
        var folder = new SceneFolder(
            SceneFolder.RootFor(parent),
            (path, e) => Plugin.Log.Warning("[scenes] skipped {Path}: {Error}", path, e.Message)
        );
        var created = !Directory.Exists(folder.Root);
        try
        {
            folder.Create();
        }
        catch (Exception e) when (SceneFolder.IsFileError(e))
        {
            return Checked($"Could not create {folder.Root}: {e.Message}");
        }

        var next = new SceneLibrary(folder, () => session.Scene, session.LoadScene);
        string? opened;
        if (move && library is { } current)
        {
            (var moved, opened) = current.MoveTo(next);
            if (!moved)
                return Checked(opened);
        }
        else
        {
            opened = next.Open(last);
        }

        library = next;
        config.SaveFolder = parent;
        config.Save();

        // The demo goes in after opening, so a new folder still opens a new empty scene.
        var refusal = Finish(opened);
        if (created || !config.DemoAdded)
            AddDemo(folder);
        return refusal;
    }

    /// <summary>Adds the demo scene the plugin carries, unless a scene has its name, and records that it has been added.</summary>
    private void AddDemo(SceneFolder folder)
    {
        var assembly = typeof(SceneFiles).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(r => r.StartsWith(DemoResource, StringComparison.Ordinal))
        )
        {
            try
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
                folder.AddScene(Path.GetFileNameWithoutExtension(resource[DemoResource.Length..]), reader.ReadToEnd());
            }
            catch (Exception e) when (SceneFolder.IsFileError(e))
            {
                Logged($"Could not add the demo scene: {e.Message}");
                return;
            }
        }

        config.DemoAdded = true;
        config.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string? Run(Func<SceneLibrary, string?> action) => library is { } l ? Finish(action(l)) : NoFolder;

    /// <summary>Updates LastScene from the open library and raises Changed, after an action already ran against it. Returns the checked refusal.</summary>
    private string? Finish(string? refusal)
    {
        refusal = Checked(refusal);
        if (library is { } l && l.CurrentName.Length > 0 && config.LastScene != l.CurrentName)
        {
            config.LastScene = l.CurrentName;
            config.Save();
        }

        if (refusal is null)
            Changed?.Invoke(this, EventArgs.Empty);
        return refusal;
    }

    /// <summary>Runs a preset file action, saying "Could not <paramref name="doing"/>" if it fails.</summary>
    private string? Files(string doing, Action<SceneFolder> action)
    {
        if (library is not { } l)
            return NoFolder;
        try
        {
            action(l.Folder);
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception e) when (SceneFolder.IsFileError(e))
        {
            return Checked($"Could not {doing}: {e.Message}");
        }
    }

    /// <summary>Asks for Setup when a refusal comes with the folder gone; the caller reports the refusal.</summary>
    private string? Checked(string? refusal)
    {
        if (refusal is not null && library is { } l && !l.Folder.Exists)
            SetupNeeded?.Invoke(this, EventArgs.Empty);
        return refusal;
    }

    /// <summary>Logs a refusal no caller reports, from loading or saving on its own, and asks for Setup when the folder has gone.</summary>
    private void Logged(string? refusal)
    {
        if (refusal is not null)
            Plugin.Log.Warning("[scenes] {Refusal}", refusal);
        Checked(refusal);
    }
}
