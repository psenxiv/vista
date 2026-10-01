using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The picker's scenes: open one, or make, rename, duplicate or delete one.</summary>
internal sealed class ScenePickerSource(SceneFiles files) : IPickerSource
{
    /// <summary>The picker's title, and the name of what opens it.</summary>
    public const string OpenScene = "Open scene";

    public string Title => OpenScene;

    public string PrimaryLabel => "Open";

    public string EmptyText => "No scenes yet.";

    public string Noun => "scene";

    public bool ShowsTracks => true;

    public bool ShowsModified => true;

    public bool ReadEachFrame => false;

    public IReadOnlyList<PickerRow> Rows() => [.. files.SceneEntries().Select(PickerRow.Of)];

    public bool IsCurrent(PickerRow row) => row.Name == files.CurrentName;

    public bool PrimaryAllowed(CameraMode mode) =>
        SceneActions.Allowed(SceneAction.Open, targetsOpenScene: false, mode);

    public string? Primary(PickerRow row) => files.Switch(row.Name);

    public bool Creates => true;

    public bool NewAllowed(CameraMode mode) => SceneActions.Allowed(SceneAction.New, targetsOpenScene: true, mode);

    public string NewSuggestion() => files.NewSuggestion();

    public string? NewRefusal(string text) => files.NameRefusal(text);

    public string? New(string name) => files.New(name);

    public bool Renames => true;

    public bool RenameAllowed(PickerRow row, CameraMode mode) =>
        SceneActions.Allowed(SceneAction.Rename, IsCurrent(row), mode);

    public string? RenameRefusal(PickerRow row, string text) => files.NameRefusal(text, renaming: row.Name);

    public string? Rename(PickerRow row, string name) => files.Rename(row.Name, name);

    public bool Duplicates => true;

    public bool DuplicateAllowed(PickerRow row, CameraMode mode) =>
        SceneActions.Allowed(SceneAction.Duplicate, IsCurrent(row), mode);

    public string CopySuggestion(PickerRow row) => files.CopySuggestion(row.Name);

    public string? DuplicateRefusal(string text) => files.NameRefusal(text);

    public string? Duplicate(PickerRow row, string name) => files.Duplicate(row.Name, name);

    public bool DeleteAllowed(PickerRow row, CameraMode mode) =>
        SceneActions.Allowed(SceneAction.Delete, IsCurrent(row), mode);

    public bool DeleteUndoable => false;

    public string? Delete(PickerRow row) => files.Delete(row.Name);

    public string? FolderTooltip => "Open scenes folder";

    public void OpenFolder() => files.OpenFolder(presets: false);
}
