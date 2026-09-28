using Vista.Core.Session;

namespace Vista.Core.Scenes;

/// <summary>Whether a scene action is allowed, so the Scene menu and the picker never write or delete a file the mode would then refuse to reopen.</summary>
public static class SceneActions
{
    /// <summary>True unless <paramref name="mode"/> is Live and <paramref name="action"/> would leave the session without its open scene: Open, New and Duplicate always load a scene, so they're refused outright; Delete only refuses when <paramref name="targetsOpenScene"/>, since deleting another scene leaves the open one alone; Rename never loads a scene, so it's always allowed.</summary>
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
