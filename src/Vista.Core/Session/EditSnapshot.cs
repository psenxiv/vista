using Vista.Core.Scenes;

namespace Vista.Core.Session;

/// <summary>The scene, the edited track and the selection, as one undo step restores them.</summary>
public readonly record struct EditSnapshot(Scene Scene, Guid Edited, SelectedItems Selection);
