using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Vista.Core.Scenes;
using Vista.Core.Session;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Windows;

/// <summary>Opens a scene, preset or playlist from a searchable list.</summary>
internal sealed class FilePickerWindow : Window
{
    private const string TitleId = "###vista-file-picker";
    private const float MinWidth = 460f;
    private const float MinHeight = 300f;
    private const float FooterButtonWidth = 90f;
    private const float NewButtonWidth = 110f;
    private const float TracksColumnWidth = 70f;
    private const float ModifiedColumnWidth = 150f;

    private readonly SessionState session;
    private readonly Dictionary<FilePickerKind, IPickerSource> sources;
    private readonly NamePrompt namePrompt = new("picker");
    private readonly DeleteConfirm deleteConfirm = new("picker");

    private FilePickerKind kind = FilePickerKind.Scene;
    private IReadOnlyList<PickerRow> rows = [];
    private string search = string.Empty;
    private string? selected;
    private bool focusSearch;

    public FilePickerWindow(GameSession game, SceneFiles files)
        : base(TitleId, ImGuiWindowFlags.NoCollapse)
    {
        session = game.State;
        sources = new()
        {
            [FilePickerKind.Scene] = new ScenePickerSource(files),
            [FilePickerKind.Preset] = new PresetPickerSource(files),
            [FilePickerKind.Playlist] = new PlaylistPickerSource(session),
        };
        WindowName = Source.Title + TitleId;
        files.Changed += (_, _) =>
        {
            if (IsOpen)
                Refresh();
        };
        RespectCloseHotkey = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(MinWidth, MinHeight);
        SizeConstraints = Layout.AtLeast(new Vector2(MinWidth, MinHeight));
    }

    private IPickerSource Source => sources[kind];

    /// <summary>Shows the window titled and listed for <paramref name="kind"/>.</summary>
    public void Show(FilePickerKind kind)
    {
        this.kind = kind;
        WindowName = Source.Title + TitleId;
        // Already open: OnOpen won't fire (no closed-to-open transition), so list here instead, once.
        if (IsOpen)
            Reset();
        IsOpen = true;
        BringToFront();
    }

    /// <summary>Lists the folder and clears the search each time the window opens.</summary>
    public override void OnOpen() => Reset();

    public override void PreDraw() => Layout.CentreOnAppearing();

    public override void Draw()
    {
        var mode = session.Mode;
        var source = Source;
        DrawSearch();
        Layout.PadLikeWindowTop();
        var listed = FileList.Filter(source.ReadEachFrame ? source.Rows() : rows, search, r => r.Name);
        // The selection only counts while its row is still visible under the current search.
        var visible = selected is { } current ? listed.FirstOrDefault(r => r.Key == current) : null;
        DrawTable(source, listed, mode);
        ImGui.Separator();
        DrawFooter(source, mode, visible);
        namePrompt.Draw();
        deleteConfirm.Draw();

        if (
            visible is { } toOpen
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && !namePrompt.Asking
            && !deleteConfirm.Asking
            && source.PrimaryAllowed(mode)
            && ImGui.IsKeyPressed(ImGuiKey.Enter)
        )
            Primary(source, toOpen);
    }

    private void Reset()
    {
        Refresh();
        search = string.Empty;
        focusSearch = true;
    }

    /// <summary>Relists the folder, dropping the selection.</summary>
    public void Refresh()
    {
        rows = Source.Rows();
        selected = null;
    }

    private void DrawSearch()
    {
        if (focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusSearch = false;
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##search", "Search", ref search, SceneNames.MaxLength);
    }

    private void DrawTable(IPickerSource source, IReadOnlyList<PickerRow> listed, CameraMode mode)
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        if (!ImGui.BeginChild("rows", new Vector2(0f, -footer)))
        {
            ImGui.EndChild();
            return;
        }

        if (listed.Count == 0)
        {
            Layout.CentredText(source.EmptyText, UiColours.Muted());
            ImGui.EndChild();
            return;
        }

        var columns = 2 + (source.ShowsTracks ? 1 : 0) + (source.ShowsModified ? 1 : 0);
        if (ImGui.BeginTable("picker", columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2f);
            if (source.ShowsTracks)
                ImGui.TableSetupColumn(
                    "Tracks",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                    TracksColumnWidth
                );
            if (source.ShowsModified)
                ImGui.TableSetupColumn(
                    "Modified",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                    ModifiedColumnWidth
                );
            ImGui.TableSetupColumn(
                "##actions",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                ActionsColumnWidth(source)
            );
            ImGui.TableHeadersRow();

            foreach (var row in listed)
                DrawRow(source, row, mode);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private static float ActionsColumnWidth(IPickerSource source)
    {
        Span<FontAwesomeIcon> icons = stackalloc FontAwesomeIcon[3];
        var count = 0;
        if (source.Renames)
            icons[count++] = FontAwesomeIcon.PencilAlt;
        if (source.Duplicates)
            icons[count++] = FontAwesomeIcon.Copy;
        icons[count++] = FontAwesomeIcon.Trash;
        return IconButton.RowWidth(icons[..count]);
    }

    private void DrawRow(IPickerSource source, PickerRow row, CameraMode mode)
    {
        using var id = ImRaii.PushId(row.Key);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var nameMin = ImGui.GetCursorScreenPos();
        var nameWidth = ImGui.GetContentRegionAvail().X;
        var picked = selected == row.Key;
        if (
            ImGui.Selectable(
                "##row",
                picked,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(0f, ImGui.GetFrameHeight())
            )
        )
            selected = row.Key;
        var rowHovered = IconButton.RowHovered(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var nameMax = nameMin + new Vector2(nameWidth, ImGui.GetFrameHeight());
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Accent, source.IsCurrent(row)))
            RowText.Draw(row.Key, row.Name, nameMin, nameMax, nameWidth);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && source.PrimaryAllowed(mode))
            Primary(source, row);
        if (ImGui.BeginPopupContextItem("row-menu"))
        {
            DrawRowMenu(source, row, mode);
            ImGui.EndPopup();
        }

        if (source.ShowsTracks)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(row.Tracks?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if (source.ShowsModified)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(row.Modified?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty);
        }

        ImGui.TableNextColumn();
        DrawRowActions(source, row, rowHovered, mode);
    }

    private void DrawRowActions(IPickerSource source, PickerRow row, bool rowHovered, CameraMode mode)
    {
        if (source.Renames)
        {
            ImGui.BeginDisabled(!source.RenameAllowed(row, mode));
            if (IconButton.RowAction("rename", FontAwesomeIcon.PencilAlt, "Rename", rowHovered))
                StartRename(source, row);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        if (source.Duplicates)
        {
            ImGui.BeginDisabled(!source.DuplicateAllowed(row, mode));
            if (IconButton.RowAction("duplicate", FontAwesomeIcon.Copy, "Duplicate", rowHovered))
                StartDuplicate(source, row);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(!source.DeleteAllowed(row, mode));
        if (IconButton.RowAction("delete", FontAwesomeIcon.Trash, "Delete", rowHovered, danger: true))
            StartDelete(source, row);
        ImGui.EndDisabled();
    }

    private void DrawRowMenu(IPickerSource source, PickerRow row, CameraMode mode)
    {
        if (source.Renames && Menu.Item("Rename", source.RenameAllowed(row, mode)))
            StartRename(source, row);
        if (source.Duplicates && Menu.Item("Duplicate", source.DuplicateAllowed(row, mode)))
            StartDuplicate(source, row);
        if (Menu.Item("Delete", source.DeleteAllowed(row, mode)))
            StartDelete(source, row);
    }

    private void DrawFooter(IPickerSource source, CameraMode mode, PickerRow? visible)
    {
        Layout.CentreRemaining(ImGui.GetFrameHeight());

        if (source.Creates)
        {
            ImGui.BeginDisabled(!source.NewAllowed(mode));
            if (ImGui.Button(NewHeading(source), new Vector2(NewButtonWidth, 0f)))
                StartNew(source);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        var folderIcon = FontAwesomeIcon.FolderOpen;
        var folderTooltip = source.FolderTooltip;
        var right = FooterButtonWidth;
        if (folderTooltip is not null)
            right += ImGui.GetStyle().ItemSpacing.X + IconButton.Width(folderIcon);
        Layout.RightAlign(right);
        if (folderTooltip is not null)
        {
            if (IconButton.Draw("open-folder", folderIcon, folderTooltip))
                source.OpenFolder();
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(!source.PrimaryAllowed(mode) || visible is null);
        if (ImGui.Button(source.PrimaryLabel, new Vector2(FooterButtonWidth, 0f)) && visible is { } toOpen)
            Primary(source, toOpen);
        ImGui.EndDisabled();
    }

    /// <summary>Runs the primary action on <paramref name="row"/>, closing the window once it succeeds.</summary>
    private void Primary(IPickerSource source, PickerRow row)
    {
        var refusal = source.Primary(row);
        Report(refusal);
        if (refusal is null)
            IsOpen = false;
    }

    private void StartRename(IPickerSource source, PickerRow row) =>
        namePrompt.Ask(
            $"Rename {source.Noun}",
            row.Name,
            text => (source.RenameRefusal(row, text), null),
            newName => Confirm(source.Rename(row, newName))
        );

    private void StartDuplicate(IPickerSource source, PickerRow row) =>
        namePrompt.Ask(
            $"Duplicate {source.Noun}",
            source.CopySuggestion(row),
            text => (source.DuplicateRefusal(text), null),
            newName => Confirm(source.Duplicate(row, newName))
        );

    private void StartDelete(IPickerSource source, PickerRow row) =>
        deleteConfirm.Ask(row.Name, source.DeleteUndoable, () => Confirm(source.Delete(row)));

    private void StartNew(IPickerSource source) =>
        namePrompt.Ask(
            NewHeading(source),
            source.NewSuggestion(),
            text => (source.NewRefusal(text), null),
            newName => Confirm(source.New(newName))
        );

    /// <summary>The New footer button's label and the new prompt's heading.</summary>
    private static string NewHeading(IPickerSource source) => $"New {source.Noun}";

    /// <summary>Reports a refusal; a successful action relists through <see cref="SceneFiles.Changed"/>, or next frame for a source read each frame.</summary>
    private static void Confirm(string? refusal) => Report(refusal);
}
