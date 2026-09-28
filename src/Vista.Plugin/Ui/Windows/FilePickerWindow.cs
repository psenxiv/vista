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

/// <summary>Opens a scene or preset from a searchable list.</summary>
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
        files.Changed += () =>
        {
            if (IsOpen)
                Refresh();
        };
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
        DrawSearch();
        Layout.PadLikeWindowTop();
        var listed = FileList.Filter(entries, search);
        // The selection only counts while its row is still visible under the current search.
        var visible = selected is { } current && listed.Any(e => e.Name == current) ? current : null;
        DrawTable(listed, mode);
        ImGui.Separator();
        DrawFooter(mode, visible);
        namePrompt.Draw();
        deleteConfirm.Draw();

        if (
            visible is { } toOpen
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && !namePrompt.Asking
            && !deleteConfirm.Asking
            && PrimaryAllowed(mode)
            && ImGui.IsKeyPressed(ImGuiKey.Enter)
        )
            Primary(toOpen);
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
        entries = kind == FilePickerKind.Scene ? files.SceneEntries() : files.PresetEntries();
        selected = null;
    }

    /// <summary>True unless <paramref name="mode"/> refuses the primary action: Live refuses opening a scene; adding a preset needs Edit, same as adding a track.</summary>
    private bool PrimaryAllowed(CameraMode mode) =>
        kind == FilePickerKind.Scene
            ? SceneActions.Allowed(SceneAction.Open, targetsOpenScene: false, mode)
            : mode == CameraMode.Editing;

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

    private void DrawTable(IReadOnlyList<FileEntry> listed, CameraMode mode)
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 2f);
        if (!ImGui.BeginChild("rows", new Vector2(0f, -footer)))
        {
            ImGui.EndChild();
            return;
        }

        if (listed.Count == 0)
        {
            DrawEmptyState();
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

            for (var i = 0; i < listed.Count; i++)
                DrawRow(listed[i], i, mode);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    /// <summary>The empty-list line, centred in the space between the search box and the footer.</summary>
    private void DrawEmptyState()
    {
        var text =
            kind == FilePickerKind.Scene
                ? "No scenes yet."
                : "No presets yet. Right-click a track and choose Save as preset.";
        var size = ImGui.CalcTextSize(text);
        Layout.CentreRemaining(size.Y);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - size.X) / 2f));
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted(text);
    }

    private float ActionsColumnWidth() =>
        kind == FilePickerKind.Scene
            ? IconButton.RowWidth(FontAwesomeIcon.PencilAlt, FontAwesomeIcon.Copy, FontAwesomeIcon.Trash)
            : IconButton.RowWidth(FontAwesomeIcon.Trash);

    private void DrawRow(FileEntry entry, int index, CameraMode mode)
    {
        using var id = ImRaii.PushId(index);
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
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(0f, ImGui.GetFrameHeight())
            )
        )
            selected = entry.Name;
        var rowHovered = IconButton.RowHovered(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var nameMax = nameMin + new Vector2(nameWidth, ImGui.GetFrameHeight());
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Accent, isOpenScene))
            RowText.Draw(entry.Name, entry.Name, nameMin, nameMax, nameWidth);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && PrimaryAllowed(mode))
            Primary(entry.Name);
        if (ImGui.BeginPopupContextItem("row-menu"))
        {
            DrawRowMenu(entry.Name, isOpenScene, mode);
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
        DrawRowActions(entry.Name, isOpenScene, rowHovered, mode);
    }

    private void DrawRowActions(string name, bool isOpenScene, bool rowHovered, CameraMode mode)
    {
        if (kind == FilePickerKind.Scene)
        {
            if (IconButton.RowAction("rename", FontAwesomeIcon.PencilAlt, "Rename", rowHovered))
                StartRename(name);
            ImGui.SameLine();
            ImGui.BeginDisabled(!SceneActions.Allowed(SceneAction.Duplicate, isOpenScene, mode));
            if (IconButton.RowAction("duplicate", FontAwesomeIcon.Copy, "Duplicate", rowHovered))
                StartDuplicate(name);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(!SceneActions.Allowed(SceneAction.Delete, isOpenScene, mode));
        if (IconButton.RowAction("delete", FontAwesomeIcon.Trash, "Delete", rowHovered, danger: true))
            StartDelete(name);
        ImGui.EndDisabled();
    }

    private void DrawRowMenu(string name, bool isOpenScene, CameraMode mode)
    {
        if (kind == FilePickerKind.Scene)
        {
            if (Menu.Item("Rename"))
                StartRename(name);
            if (Menu.Item("Duplicate", SceneActions.Allowed(SceneAction.Duplicate, isOpenScene, mode)))
                StartDuplicate(name);
        }

        if (Menu.Item("Delete", SceneActions.Allowed(SceneAction.Delete, isOpenScene, mode)))
            StartDelete(name);
    }

    private void DrawFooter(CameraMode mode, string? visible)
    {
        Layout.CentreRemaining(ImGui.GetFrameHeight());

        if (kind == FilePickerKind.Scene)
        {
            ImGui.BeginDisabled(!SceneActions.Allowed(SceneAction.New, targetsOpenScene: true, mode));
            if (ImGui.Button("New scene", new Vector2(NewSceneButtonWidth, 0f)))
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

        ImGui.BeginDisabled(!PrimaryAllowed(mode) || visible is null);
        if (
            ImGui.Button(kind == FilePickerKind.Scene ? "Open" : "Add", new Vector2(FooterButtonWidth, 0f))
            && visible is { } toOpen
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

    /// <summary>Reports a refusal; a successful action relists through <see cref="SceneFiles.Changed"/>.</summary>
    private static void Confirm(string? refusal) => Report(refusal);
}
