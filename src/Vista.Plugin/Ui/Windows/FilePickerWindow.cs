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

/// <summary>Opens a scene or preset from a searchable list: double-click, Enter or the primary button opens or adds it; Rename, Duplicate and Delete act on a row; New scene and the folder sit in the footer.</summary>
internal sealed class FilePickerWindow : Window
{
    private const string TitleId = "###vista-file-picker";
    private const float MinWidth = 460f;
    private const float MinHeight = 300f;
    private const float FooterButtonWidth = 90f;
    private const float NewSceneButtonWidth = 110f;
    private const float TracksColumnWidth = 70f;
    private const float ModifiedColumnWidth = 150f;

    private readonly SessionState session;
    private readonly SceneFiles files;
    private readonly NamePrompt namePrompt = new("picker");
    private readonly DeleteConfirm deleteConfirm = new("picker");

    private FilePickerKind kind = FilePickerKind.Scene;
    private IReadOnlyList<FileEntry> entries = [];
    private string search = string.Empty;
    private string? selected;
    private bool focusSearch;

    public FilePickerWindow(GameSession game, SceneFiles files)
        : base("Open scene" + TitleId, ImGuiWindowFlags.NoCollapse)
    {
        session = game.State;
        this.files = files;
        RespectCloseHotkey = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(MinWidth, MinHeight);
        SizeConstraints = Layout.AtLeast(new Vector2(MinWidth, MinHeight));
    }

    /// <summary>Shows the window titled and listed for <paramref name="kind"/>.</summary>
    public void Show(FilePickerKind kind)
    {
        this.kind = kind;
        WindowName = (kind == FilePickerKind.Scene ? "Open scene" : "Add preset") + TitleId;
        Reset();
        IsOpen = true;
        BringToFront();
    }

    /// <summary>Lists the folder and clears the search each time the window opens.</summary>
    public override void OnOpen() => Reset();

    public override void PreDraw() => Layout.CentreOnAppearing();

    public override void Draw()
    {
        var live = kind == FilePickerKind.Scene && session.Mode == CameraMode.Live;
        DrawSearch();
        var listed = FileList.Filter(entries, search);
        DrawTable(listed, live);
        ImGui.Separator();
        DrawFooter(live);
        namePrompt.Draw();
        deleteConfirm.Draw();

        if (
            selected is { } current
            && !live
            && !namePrompt.Asking
            && !deleteConfirm.Asking
            && listed.Any(e => e.Name == current)
            && ImGui.IsKeyPressed(ImGuiKey.Enter)
        )
            Primary(current);
    }

    private void Reset()
    {
        Refresh();
        search = string.Empty;
        focusSearch = true;
    }

    private void Refresh()
    {
        entries = kind == FilePickerKind.Scene ? files.SceneEntries() : files.PresetEntries();
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

    private void DrawTable(IReadOnlyList<FileEntry> listed, bool live)
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        if (!ImGui.BeginChild("rows", new Vector2(0f, -footer)))
        {
            ImGui.EndChild();
            return;
        }

        if (listed.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
                ImGui.TextUnformatted(
                    kind == FilePickerKind.Scene
                        ? "No scenes yet."
                        : "No presets yet. Right-click a track and choose Save as preset."
                );
            ImGui.EndChild();
            return;
        }

        var columns = kind == FilePickerKind.Scene ? 4 : 3;
        if (ImGui.BeginTable("picker", columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2f);
            if (kind == FilePickerKind.Scene)
                ImGui.TableSetupColumn(
                    "Tracks",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                    TracksColumnWidth
                );
            ImGui.TableSetupColumn(
                "Modified",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                ModifiedColumnWidth
            );
            ImGui.TableSetupColumn(
                "##actions",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                ActionsColumnWidth()
            );
            ImGui.TableHeadersRow();

            foreach (var entry in listed)
                DrawRow(entry, live);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private float ActionsColumnWidth() =>
        kind == FilePickerKind.Scene
            ? IconButton.RowWidth(FontAwesomeIcon.PencilAlt, FontAwesomeIcon.Copy, FontAwesomeIcon.Trash)
            : IconButton.RowWidth(FontAwesomeIcon.Trash);

    private void DrawRow(FileEntry entry, bool live)
    {
        using var id = ImRaii.PushId(entry.Name);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var isOpenScene = kind == FilePickerKind.Scene && entry.Name == files.CurrentName;
        var nameMin = ImGui.GetCursorScreenPos();
        var nameWidth = ImGui.GetContentRegionAvail().X;
        var picked = selected == entry.Name;
        if (
            ImGui.Selectable(
                "##row",
                picked,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap
            )
        )
            selected = entry.Name;
        var rowHovered = IconButton.RowHovered(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var nameMax = nameMin + new Vector2(nameWidth, ImGui.GetFrameHeight());
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Accent, isOpenScene))
            RowText.Draw(entry.Name, entry.Name, nameMin, nameMax, nameWidth);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !live)
            Primary(entry.Name);
        if (ImGui.BeginPopupContextItem("row-menu"))
        {
            DrawRowMenu(entry.Name, live);
            ImGui.EndPopup();
        }

        if (kind == FilePickerKind.Scene)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(entry.Tracks?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(entry.Modified.ToString("g", CultureInfo.CurrentCulture));

        ImGui.TableNextColumn();
        DrawRowActions(entry.Name, rowHovered, live);
    }

    private void DrawRowActions(string name, bool rowHovered, bool live)
    {
        if (kind == FilePickerKind.Scene)
        {
            if (IconButton.RowAction("rename", FontAwesomeIcon.PencilAlt, "Rename...", rowHovered))
                StartRename(name);
            ImGui.SameLine();
            ImGui.BeginDisabled(live);
            if (IconButton.RowAction("duplicate", FontAwesomeIcon.Copy, "Duplicate...", rowHovered))
                StartDuplicate(name);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        if (IconButton.RowAction("delete", FontAwesomeIcon.Trash, "Delete...", rowHovered, danger: true))
            StartDelete(name);
    }

    private void DrawRowMenu(string name, bool live)
    {
        if (kind == FilePickerKind.Scene)
        {
            if (Menu.Item("Rename..."))
                StartRename(name);
            if (Menu.Item("Duplicate...", !live))
                StartDuplicate(name);
        }

        if (Menu.Item("Delete..."))
            StartDelete(name);
    }

    private void DrawFooter(bool live)
    {
        if (kind == FilePickerKind.Scene)
        {
            ImGui.BeginDisabled(live);
            if (ImGui.Button("New scene...", new Vector2(NewSceneButtonWidth, 0f)))
                StartNew();
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        var folderIcon = FontAwesomeIcon.FolderOpen;
        var folderTooltip = kind == FilePickerKind.Scene ? "Open scenes folder" : "Open presets folder";
        Layout.RightAlign(FooterButtonWidth + ImGui.GetStyle().ItemSpacing.X + IconButton.Width(folderIcon));
        if (IconButton.Draw("open-folder", folderIcon, folderTooltip))
            files.OpenFolder(presets: kind == FilePickerKind.Preset);
        ImGui.SameLine();

        ImGui.BeginDisabled((kind == FilePickerKind.Scene && live) || selected is null);
        if (
            ImGui.Button(kind == FilePickerKind.Scene ? "Open" : "Add", new Vector2(FooterButtonWidth, 0f))
            && selected is { } toOpen
        )
            Primary(toOpen);
        ImGui.EndDisabled();
    }

    /// <summary>Opens (scenes) or adds (presets) <paramref name="name"/>, closing the window once it succeeds.</summary>
    private void Primary(string name)
    {
        var refusal = kind == FilePickerKind.Scene ? files.Switch(name) : files.AddPreset(name);
        Report(refusal);
        if (refusal is null)
            IsOpen = false;
    }

    private void StartRename(string name) =>
        namePrompt.Ask(
            "Rename scene",
            name,
            text => (files.NameRefusal(text, renaming: name), null),
            newName => Confirm(files.Rename(name, newName))
        );

    private void StartDuplicate(string name) =>
        namePrompt.Ask(
            "Duplicate scene",
            files.CopySuggestion(name),
            text => (files.NameRefusal(text), null),
            newName => Confirm(files.Duplicate(name, newName))
        );

    private void StartDelete(string name) =>
        deleteConfirm.Ask(
            name,
            () => Confirm(kind == FilePickerKind.Scene ? files.Delete(name) : files.DeletePreset(name))
        );

    private void StartNew() =>
        namePrompt.Ask(
            "New scene",
            files.NewSuggestion(),
            text => (files.NameRefusal(text), null),
            newName => Confirm(files.New(newName))
        );

    /// <summary>Reports a refusal, then relists the folder and drops the selection, since the action may have added, renamed or removed a file.</summary>
    private void Confirm(string? refusal)
    {
        Report(refusal);
        Refresh();
    }
}
