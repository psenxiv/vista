using Vista.Core.Scenes;

namespace Vista.Tests.Scenes;

/// <summary>Scenes told apart by their first track's name, and a scene file in an older format.</summary>
internal static class SceneFixtures
{
    /// <summary>A new scene whose one track is called <paramref name="trackName"/>.</summary>
    internal static Scene Named(string trackName)
    {
        var scene = SceneEditing.New();
        return SceneEditing.Rename(scene, scene.Tracks[0].Id, trackName);
    }

    /// <summary>A scene file saved in format 1, when a scene held one playlist; its playlist loops.</summary>
    internal static string FormatOneSceneJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenes", "Fixtures", "format-1-scene.json"));

    /// <summary>The name of the first track in the scene file <paramref name="name"/>.</summary>
    internal static string FirstTrackIn(TempFolder temp, string name) => temp.Folder.LoadScene(name).Tracks[0].Name;
}
