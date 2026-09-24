using System.Numerics;
using Vista.Core.Scenes;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui.Windows;

/// <summary>Asks for the folder Vista saves in, with Dalamud's folder picker; closed without choosing, it comes back the next time Vista opens.</summary>
internal sealed class SetupWindow : Window
{
    private const float Width = 440f;
    private const float OkWidth = 90f;
    private const string FirstRun = "Vista needs a folder to save your scenes and presets. Choose one to continue.";
    private const string Gone = "Vista can't find its save folder. Choose it again, or a new one, to continue.";

    private readonly SceneFiles files;
    private readonly Action continued;
    private readonly FileDialogManager picker = new();
    private string? parent;

    public SetupWindow(SceneFiles files, Action continued)
        : base("Vista Setup###vista-setup", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.files = files;
        this.continued = continued;
        RespectCloseHotkey = false;
    }

    /// <summary>Opens on the folder in use when it works, otherwise with nothing chosen.</summary>
    public override void OnOpen() => parent = files.Ready ? files.Chosen : null;

    /// <summary>Centres the window the first time it appears.</summary>
    public override void PreDraw()
        => ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width);
        ImGui.TextUnformatted(files.Lost ? Gone : FirstRun);
        ImGui.Spacing();

        if (IconButton.Draw("choose-folder", FontAwesomeIcon.Folder, "Choose folder"))
            picker.OpenFolderDialog("Choose folder", (ok, path) => { if (ok) parent = path; }, files.Chosen);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, parent is null ? UiColours.Muted() : UiColours.Accent))
            ImGui.TextUnformatted(parent is null ? "No folder chosen" : SceneFolder.RootFor(parent));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Width - OkWidth);
        ImGui.BeginDisabled(parent is null);
        if (ImGui.Button("Ok", new Vector2(OkWidth, 0f)) && parent is not null && files.Choose(parent) is null && files.Ready)
        {
            IsOpen = false;
            continued();
        }

        ImGui.EndDisabled();
        picker.Draw();
    }
}
