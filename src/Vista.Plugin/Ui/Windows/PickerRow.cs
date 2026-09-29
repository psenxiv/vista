using Vista.Core.Scenes;

namespace Vista.Plugin.Ui.Windows;

/// <summary>One row in the picker; <see cref="Key"/> keys its selection and ImGui ids.</summary>
internal sealed record PickerRow(string Key, string Name, int? Tracks, DateTime? Modified)
{
    /// <summary>A file's row, keyed by its name.</summary>
    public static PickerRow Of(FileEntry entry) => new(entry.Name, entry.Name, entry.Tracks, entry.Modified);
}
