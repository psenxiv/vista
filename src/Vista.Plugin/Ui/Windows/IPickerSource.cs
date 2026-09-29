using Vista.Core.Session;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui.Windows;

/// <summary>What the picker lists for one kind, and what its actions do.</summary>
internal interface IPickerSource
{
    /// <summary>The window title, without its id.</summary>
    string Title { get; }

    /// <summary>The footer button that opens or adds the selected row.</summary>
    string PrimaryLabel { get; }

    /// <summary>The line shown when no rows are listed.</summary>
    string EmptyText { get; }

    /// <summary>What a row is, in the prompt headings.</summary>
    string Noun { get; }

    /// <summary>Whether the Tracks column shows.</summary>
    bool ShowsTracks { get; }

    /// <summary>Whether the Modified column shows.</summary>
    bool ShowsModified { get; }

    /// <summary>Whether the rows are read every frame rather than on opening and on <see cref="SceneFiles.Changed"/>.</summary>
    bool ReadEachFrame { get; }

    /// <summary>Every row, unfiltered.</summary>
    IReadOnlyList<PickerRow> Rows();

    /// <summary>Whether <paramref name="row"/> is the open one, drawn in the accent colour.</summary>
    bool IsCurrent(PickerRow row);

    /// <summary>Whether <paramref name="mode"/> allows the primary action.</summary>
    bool PrimaryAllowed(CameraMode mode);

    /// <summary>Opens or adds <paramref name="row"/>; the refusal, or null.</summary>
    string? Primary(PickerRow row);

    /// <summary>The footer button that makes a new row; null hides it.</summary>
    string? NewLabel { get; }

    /// <summary>Whether <paramref name="mode"/> allows making a new row.</summary>
    bool NewAllowed(CameraMode mode);

    /// <summary>The name the new prompt suggests.</summary>
    string NewSuggestion();

    /// <summary>Why <paramref name="text"/> can't name a new row, or null.</summary>
    string? NewRefusal(string text);

    /// <summary>Makes a row named <paramref name="name"/>; the refusal, or null.</summary>
    string? New(string name);

    /// <summary>Whether rows can be renamed; false hides the action and menu item.</summary>
    bool Renames { get; }

    /// <summary>Whether <paramref name="mode"/> allows renaming <paramref name="row"/>.</summary>
    bool RenameAllowed(PickerRow row, CameraMode mode);

    /// <summary>Why <paramref name="text"/> can't rename <paramref name="row"/>, or null.</summary>
    string? RenameRefusal(PickerRow row, string text);

    /// <summary>Renames <paramref name="row"/> to <paramref name="name"/>; the refusal, or null.</summary>
    string? Rename(PickerRow row, string name);

    /// <summary>Whether rows can be duplicated; false hides the action and menu item.</summary>
    bool Duplicates { get; }

    /// <summary>Whether <paramref name="mode"/> allows duplicating <paramref name="row"/>.</summary>
    bool DuplicateAllowed(PickerRow row, CameraMode mode);

    /// <summary>The name the duplicate prompt suggests for <paramref name="row"/>.</summary>
    string CopySuggestion(PickerRow row);

    /// <summary>Why <paramref name="text"/> can't name a copy, or null.</summary>
    string? DuplicateRefusal(string text);

    /// <summary>Copies <paramref name="row"/> as <paramref name="name"/>; the refusal, or null.</summary>
    string? Duplicate(PickerRow row, string name);

    /// <summary>Whether <paramref name="mode"/> allows deleting <paramref name="row"/>.</summary>
    bool DeleteAllowed(PickerRow row, CameraMode mode);

    /// <summary>Whether a delete can be undone, which drops "This can't be undone." from the confirmation.</summary>
    bool DeleteUndoable { get; }

    /// <summary>Deletes <paramref name="row"/>; the refusal, or null.</summary>
    string? Delete(PickerRow row);

    /// <summary>The folder button's tooltip; null hides it.</summary>
    string? FolderTooltip { get; }

    /// <summary>Opens the rows' folder in the system's file browser.</summary>
    void OpenFolder();
}
