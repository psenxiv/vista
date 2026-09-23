using System.Diagnostics;
using Vista.Core.Scenes;

namespace Vista.Plugin.Session;

/// <summary>The save folder and the open scene's library, kept in step with the settings; asks for Setup when the folder is gone.</summary>
internal sealed class SceneFiles
{
    private readonly Configuration config;
    private readonly CameraSession session;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private SceneLibrary? library;
    private string? tickRefusal;

    public SceneFiles(Configuration config, CameraSession session)
    {
        this.config = config;
        this.session = session;
        if (config.SaveFolder is { } parent && Directory.Exists(SceneFolder.RootFor(parent))) Use(parent, config.LastScene);
    }

    /// <summary>Raised when the folder is missing and Setup should be shown.</summary>
    public event Action? SetupNeeded;

    /// <summary>The parent folder shown in Setup: the chosen one, or the plugin's config folder.</summary>
    public string Parent => config.SaveFolder ?? Plugin.PluginInterface.GetPluginConfigDirectory();

    /// <summary>True when a folder is chosen and its vistaxiv folder is there.</summary>
    public bool Ready => library is { } l && l.Folder.Exists;

    /// <summary>The open scene's name, or empty before one is open.</summary>
    public string CurrentName => library?.CurrentName ?? string.Empty;

    /// <summary>The scene names in the folder, read now.</summary>
    public IReadOnlyList<string> Scenes() => library?.Scenes() ?? [];

    /// <summary>Why <paramref name="name"/> can't name a new scene, or with <paramref name="renaming"/> the open one, or null.</summary>
    public string? NameRefusal(string name, bool renaming = false) => library?.NameRefusal(name, renaming) ?? SceneNames.Refusal(name);

    /// <summary>The preset names in the folder, read now.</summary>
    public IReadOnlyList<string> Presets() => library?.Folder.PresetNames() ?? [];

    /// <summary>Uses <paramref name="parent"/>'s vistaxiv folder, creating it, after saving the open scene where it was; a new folder opens its first scene.</summary>
    public string? Choose(string parent)
    {
        var changed = config.SaveFolder is not { } old || !string.Equals(Path.GetFullPath(old), Path.GetFullPath(parent), StringComparison.Ordinal);
        if (!changed && Ready) return null;

        // The same folder, gone from disk: put the open scene back in it rather than start empty.
        if (!changed && library is { } lost && lost.CurrentName.Length > 0) return Report(lost.Recreate());

        if (library is { } current && changed) current.SaveNow();
        return Use(parent, changed && config.SaveFolder is not null ? null : config.LastScene);
    }

    public string? Switch(string name) => Run(l => l.Switch(name));

    public string? New(string name) => Run(l => l.New(name));

    public string? Rename(string name) => Run(l => l.Rename(name));

    public string? Duplicate(string name) => Run(l => l.Duplicate(name));

    public string? Delete() => Run(l => l.Delete());

    /// <summary>Saves the open scene now if it has changed.</summary>
    public string? SaveNow() => Run(l => l.SaveNow());

    /// <summary>Saves the open scene once it has been still long enough. Call once a frame; a failing save retries each frame but reports once.</summary>
    public void Tick()
    {
        if (library is null) return;
        var refusal = library.Tick(clock.Elapsed.TotalSeconds);
        if (refusal != tickRefusal && refusal is not null) Report(refusal);
        tickRefusal = refusal;
    }

    /// <summary>Saves track <paramref name="trackId"/> as the preset <paramref name="name"/>, replacing one of that name.</summary>
    public string? SavePreset(string name, Guid trackId) => Files(folder => folder.SavePreset(name.Trim(), session.PresetOf(trackId)));

    /// <summary>Adds the preset <paramref name="name"/> to the scene under the camera.</summary>
    public string? AddPreset(string name)
    {
        if (library is not { } l) return "No save folder is chosen.";
        Preset preset;
        try { preset = l.Folder.LoadPreset(name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return Report($"Could not open preset {name}: {e.Message}"); }
        return session.AddPreset(preset);
    }

    public string? DeletePreset(string name) => Files(folder => folder.DeletePreset(name));

    private string? Use(string parent, string? last)
    {
        var folder = new SceneFolder(SceneFolder.RootFor(parent), (path, e) => Plugin.Log.Warning("[scenes] skipped {Path}: {Error}", path, e.Message));
        try { folder.Create(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Report($"Could not create {folder.Root}: {e.Message}"); }

        library = new SceneLibrary(folder, () => session.Scene, session.LoadScene);
        config.SaveFolder = parent;
        config.Save();
        return Run(l => l.Open(last));
    }

    private string? Run(Func<SceneLibrary, string?> action)
    {
        if (library is not { } l) return "No save folder is chosen.";
        var refusal = Report(action(l));
        if (l.CurrentName.Length > 0 && config.LastScene != l.CurrentName)
        {
            config.LastScene = l.CurrentName;
            config.Save();
        }

        return refusal;
    }

    private string? Files(Action<SceneFolder> action)
    {
        if (library is not { } l) return "No save folder is chosen.";
        try { action(l.Folder); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Report($"Could not save: {e.Message}"); }
    }

    /// <summary>Logs a refusal, and asks for Setup when the folder has gone.</summary>
    private string? Report(string? refusal)
    {
        if (refusal is null) return null;
        Plugin.Log.Warning("[scenes] {Refusal}", refusal);
        if (library is { } l && !l.Folder.Exists) SetupNeeded?.Invoke();
        return refusal;
    }
}
