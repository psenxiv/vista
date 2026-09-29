using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The picker's presets: add one under the camera, or delete one.</summary>
internal sealed class PresetPickerSource(SceneFiles files) : IPickerSource
{
    public string Title => "Add preset";

    public string PrimaryLabel => "Add";

    public string EmptyText => "No presets yet. Right-click a track to save one.";

    public string Noun => "preset";

    public bool ShowsTracks => false;

    public bool ShowsModified => true;

    public bool ReadEachFrame => false;

    public IReadOnlyList<PickerRow> Rows() => [.. files.PresetEntries().Select(PickerRow.Of)];

    public bool IsCurrent(PickerRow row) => false;

    /// <summary>Only Edit allows adding a preset.</summary>
    public bool PrimaryAllowed(CameraMode mode) => mode == CameraMode.Editing;

    public string? Primary(PickerRow row) => files.AddPreset(row.Name);

    public bool Creates => false;

    public bool NewAllowed(CameraMode mode) => false;

    public string NewSuggestion() => throw new NotSupportedException();

    public string? NewRefusal(string text) => throw new NotSupportedException();

    public string? New(string name) => throw new NotSupportedException();

    public bool Renames => false;

    public bool RenameAllowed(PickerRow row, CameraMode mode) => false;

    public string? RenameRefusal(PickerRow row, string text) => throw new NotSupportedException();

    public string? Rename(PickerRow row, string name) => throw new NotSupportedException();

    public bool Duplicates => false;

    public bool DuplicateAllowed(PickerRow row, CameraMode mode) => false;

    public string CopySuggestion(PickerRow row) => throw new NotSupportedException();

    public string? DuplicateRefusal(string text) => throw new NotSupportedException();

    public string? Duplicate(PickerRow row, string name) => throw new NotSupportedException();

    public bool DeleteAllowed(PickerRow row, CameraMode mode) =>
        SceneActions.Allowed(SceneAction.Delete, IsCurrent(row), mode);

    public bool DeleteUndoable => false;

    public string? Delete(PickerRow row) => files.DeletePreset(row.Name);

    public string? FolderTooltip => "Open presets folder";

    public void OpenFolder() => files.OpenFolder(presets: true);
}
