namespace Vista.Core.Scenes;

/// <summary>What choosing a save folder does, given the one chosen before.</summary>
public enum FolderChange
{
    /// <summary>The open folder was chosen again: nothing changes.</summary>
    Keep,

    /// <summary>The open folder has gone and a scene is open: create it again and write the scene into it.</summary>
    Recreate,

    /// <summary>Open the chosen folder at the last scene: the first choice, or the same folder with no scene open.</summary>
    Reopen,

    /// <summary>Another folder: leave the open one for it.</summary>
    Move,
}
