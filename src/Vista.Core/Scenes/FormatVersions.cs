namespace Vista.Core.Scenes;

/// <summary>The record of the format each kind of file was last fully upgraded to, and when an upgrade pass is due.</summary>
public static class FormatVersions
{
    /// <summary>The record's key for scene files.</summary>
    public const string Scenes = "scenes";

    /// <summary>True when the scene entry, or the first format when it is missing, is below the format this version writes.</summary>
    public static bool SceneUpgradeDue(IReadOnlyDictionary<string, int> versions) =>
        versions.GetValueOrDefault(Scenes, SceneJson.FirstSceneFormat) < SceneJson.SceneFormat;

    /// <summary>Sets the scene entry to the format this version writes.</summary>
    public static void RecordScenes(IDictionary<string, int> versions) => versions[Scenes] = SceneJson.SceneFormat;
}
