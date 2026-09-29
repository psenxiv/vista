using Dalamud.Configuration;
using Vista.Core.Display;

namespace Vista.Plugin;

/// <summary>Vista's saved settings, kept by Dalamud in the plugin's config file.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>True once the welcome screen has been dismissed on this install.</summary>
    public bool WelcomeSeen { get; set; }

    /// <summary>The folder Vista keeps its vistaxiv folder in, or null until chosen.</summary>
    public string? SaveFolder { get; set; }

    /// <summary>The name of the scene last open, reopened on load.</summary>
    public string? LastScene { get; set; }

    /// <summary>True once the demo scene has been added to a save folder; after that only folders Vista creates get it.</summary>
    public bool DemoAdded { get; set; }

    /// <summary>The format each kind of file was last fully upgraded to; a missing entry means the first format.</summary>
    public IDictionary<string, int> Versions { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The Hierarchy panel's width, in pixels.</summary>
    public float HierarchyWidth { get; set; } = PanelWidth.Default;

    /// <summary>The Playlist panel's width, in pixels.</summary>
    public float PlaylistWidth { get; set; } = PanelWidth.Default;

    /// <summary>True to hide the game's UI while Live plays.</summary>
    public bool HideUiInLive { get; set; }

    /// <summary>True to show track names above their anchors.</summary>
    public bool ShowTrackNames { get; set; } = true;

    /// <summary>The track names' size, as a multiple of the font size.</summary>
    public float TrackNameScale { get; set; } = TrackNameSize.Default;

    /// <summary>Writes the settings to disk.</summary>
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
