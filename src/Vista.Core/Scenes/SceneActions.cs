using Vista.Core.Session;

namespace Vista.Core.Scenes;

/// <summary>Which scene actions each camera mode allows.</summary>
public static class SceneActions
{
    /// <summary>Whether <paramref name="action"/> is allowed in <paramref name="mode"/>, given whether it targets the open scene.</summary>
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
