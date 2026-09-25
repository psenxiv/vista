using System.Diagnostics;
using Vista.Core.Scenes;
using Vista.Core.Session;

namespace Vista.Plugin.Session;

/// <summary>The save folder and the open scene's library, kept in step with the settings; asks for Setup when the folder is gone.</summary>
internal sealed class SceneFiles
{
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
            Use(parent, config.LastScene);
    }

    /// <summary>Raised when the folder is missing and Setup should be shown.</summary>
    public event EventHandler? SetupNeeded;

    /// <summary>The folder chosen before, or null; where the folder picker starts.</summary>
    public string? Chosen => config.SaveFolder;

    /// <summary>True when a folder was chosen but its vistaxiv folder has gone.</summary>
    public bool Lost => config.SaveFolder is not null && !Ready;

    /// <summary>True when a folder is chosen and its vistaxiv folder is there.</summary>
    public bool Ready => library is { } l && l.Folder.Exists;

    /// <summary>The open scene's name, or empty before one is open.</summary>
    public string CurrentName => library?.CurrentName ?? string.Empty;

    /// <summary>The scene names in the folder, read now.</summary>
    public IReadOnlyList<string> Scenes() => library?.Scenes() ?? [];

    /// <summary>Why <paramref name="name"/> can't name a new scene, or with <paramref name="renaming"/> the open one, or null.</summary>
    public string? NameRefusal(string name, bool renaming = false) =>
        library?.NameRefusal(name, renaming) ?? SceneNames.Refusal(name);

    /// <summary>The name "New scene" suggests, read now; empty with no folder.</summary>
    public string NewSuggestion() => library?.NewSuggestion() ?? string.Empty;

    /// <summary>The name "Duplicate scene" suggests, read now; empty with no folder.</summary>
    public string CopySuggestion() => library?.CopySuggestion() ?? string.Empty;

    /// <summary>The preset names in the folder, read now.</summary>
    public IReadOnlyList<string> PresetNames() => library?.Folder.PresetNames() ?? [];

    /// <summary>Uses <paramref name="parent"/>'s vistaxiv folder, creating it, after saving the open scene where it was; a new folder opens its first scene.</summary>
    public string? Choose(string parent)
    {
        var changed =
            config.SaveFolder is not { } old
            || !string.Equals(Path.GetFullPath(old), Path.GetFullPath(parent), StringComparison.Ordinal);
        if (!changed && Ready)
            return null;

        // The same folder, gone from disk: put the open scene back in it rather than start empty.
        if (!changed && library is { } lost && lost.CurrentName.Length > 0)
        {
            var refusal = Report(lost.Recreate());
            if (refusal is null)
                AddDemo(lost.Folder);
            return refusal;
        }

        if (library is { } current && changed)
            current.SaveNow();
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
        if (library is null)
            return;
        var refusal = library.Tick(clock.Elapsed.TotalSeconds);
        if (refusal != tickRefusal && refusal is not null)
            Report(refusal);
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
            return "No save folder is chosen.";
        Preset preset;
        try
        {
            preset = l.Folder.LoadPreset(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Report($"Could not add preset {name}: {e.Message}");
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

    private string? Use(string parent, string? last)
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Report($"Could not create {folder.Root}: {e.Message}");
        }

        library = new SceneLibrary(folder, () => session.Scene, session.LoadScene);
        config.SaveFolder = parent;
        config.Save();

        // The demo goes in after opening, so a new folder still opens a new empty scene.
        var refusal = Run(l => l.Open(last));
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
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Report($"Could not add the demo scene: {e.Message}");
                return;
            }
        }

        config.DemoAdded = true;
        config.Save();
    }

    private string? Run(Func<SceneLibrary, string?> action)
    {
        if (library is not { } l)
            return "No save folder is chosen.";
        var refusal = Report(action(l));
        if (l.CurrentName.Length > 0 && config.LastScene != l.CurrentName)
        {
            config.LastScene = l.CurrentName;
            config.Save();
        }

        return refusal;
    }

    /// <summary>Runs a preset file action, saying "Could not <paramref name="doing"/>" if it fails.</summary>
    private string? Files(string doing, Action<SceneFolder> action)
    {
        if (library is not { } l)
            return "No save folder is chosen.";
        try
        {
            action(l.Folder);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Report($"Could not {doing}: {e.Message}");
        }
    }

    /// <summary>Logs a refusal, and asks for Setup when the folder has gone.</summary>
    private string? Report(string? refusal)
    {
        if (refusal is null)
            return null;
        Plugin.Log.Warning("[scenes] {Refusal}", refusal);
        if (library is { } l && !l.Folder.Exists)
            SetupNeeded?.Invoke(this, EventArgs.Empty);
        return refusal;
    }
}
