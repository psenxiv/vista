using Vista.Core.Session;

namespace Vista.Core.Scenes;

/// <summary>Which scene actions each camera mode allows.</summary>
public static class SceneActions
{
    /// <summary>False in Live for Open, New, Duplicate and deleting the open scene, since Live refuses the scene load each ends with.</summary>
    public static bool Allowed(SceneAction action, bool targetsOpenScene, CameraMode mode)
    {
        if (mode != CameraMode.Live)
            return true;
        return action switch
        {
            SceneAction.Rename => true,
            SceneAction.Delete => !targetsOpenScene,
            _ => false,
        };
    }
}
