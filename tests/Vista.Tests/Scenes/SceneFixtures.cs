using Vista.Core.Scenes;

namespace Vista.Tests.Scenes;

/// <summary>Scenes told apart by their first track's name.</summary>
internal static class SceneFixtures
{
    /// <summary>A new scene whose one track is called <paramref name="trackName"/>.</summary>
    internal static Scene Named(string trackName)
    {
        var scene = SceneEditing.New();
        return SceneEditing.Rename(scene, scene.Tracks[0].Id, trackName);
    }

    /// <summary>The name of the first track in the scene file <paramref name="name"/>.</summary>
    internal static string FirstTrackIn(TempFolder temp, string name) => temp.Folder.LoadScene(name).Tracks[0].Name;
}
