using Dalamud.Configuration;

namespace Vista.Plugin;

/// <summary>Vista's saved settings, kept by Dalamud in the plugin's config file.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>True once the welcome screen has been dismissed on this install.</summary>
    public bool WelcomeSeen { get; set; }

    /// <summary>Writes the settings to disk.</summary>
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
