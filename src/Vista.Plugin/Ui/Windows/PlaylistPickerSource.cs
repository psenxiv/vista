using Vista.Core.Scenes;
using Vista.Core.Session;

namespace Vista.Plugin.Ui.Windows;

/// <summary>The picker's playlists in the open scene: select one, or make, rename, duplicate or delete one; Edit only.</summary>
internal sealed class PlaylistPickerSource(SessionState session) : IPickerSource
{
    /// <summary>The window title, and the tooltip of the button that opens it.</summary>
    public const string OpenPlaylist = "Open playlist";

    public string Title => OpenPlaylist;

    public string PrimaryLabel => "Open";

    public string EmptyText => "No playlists match.";

    public string Noun => "playlist";

    public bool ShowsTracks => false;

    public bool ShowsModified => false;

    public bool ReadEachFrame => true;

    public IReadOnlyList<PickerRow> Rows() =>
        [.. session.Scene.Playlists.Select(p => new PickerRow(p.Id.ToString(), p.Name, null, null))];

    public bool IsCurrent(PickerRow row) => Id(row) == session.Scene.SelectedPlaylistId;

    public bool PrimaryAllowed(CameraMode mode) => Editing(mode);

    public string? Primary(PickerRow row) => session.SelectPlaylist(Id(row));

    public bool Creates => true;

    public bool NewAllowed(CameraMode mode) => Editing(mode);

    public string NewSuggestion() => PlaylistEditing.NewSuggestion(session.Scene);

    public string? NewRefusal(string text) => PlaylistEditing.NameRefusal(session.Scene, text);

    public string? New(string name) => session.NewPlaylist(name);

    public bool Renames => true;

    public bool RenameAllowed(PickerRow row, CameraMode mode) => Editing(mode);

    public string? RenameRefusal(PickerRow row, string text) =>
        PlaylistEditing.NameRefusal(session.Scene, text, renaming: Id(row));

    public string? Rename(PickerRow row, string name) => session.RenamePlaylist(Id(row), name);

    public bool Duplicates => true;

    public bool DuplicateAllowed(PickerRow row, CameraMode mode) => Editing(mode);

    public string CopySuggestion(PickerRow row) => PlaylistEditing.CopySuggestion(session.Scene, Id(row));

    public string? DuplicateRefusal(string text) => PlaylistEditing.NameRefusal(session.Scene, text);

    public string? Duplicate(PickerRow row, string name) => session.DuplicatePlaylist(Id(row), name);

    public bool DeleteAllowed(PickerRow row, CameraMode mode) =>
        Editing(mode) && PlaylistEditing.CanDelete(session.Scene);

    public bool DeleteUndoable => true;

    public string? Delete(PickerRow row) => session.DeletePlaylist(Id(row));

    public string? FolderTooltip => null;

    public void OpenFolder() => throw new NotSupportedException();

    /// <summary>Every playlist action is Edit only.</summary>
    private static bool Editing(CameraMode mode) => mode == CameraMode.Editing;

    /// <summary>The playlist id a row is keyed by.</summary>
    private static Guid Id(PickerRow row) => Guid.Parse(row.Key);
}
