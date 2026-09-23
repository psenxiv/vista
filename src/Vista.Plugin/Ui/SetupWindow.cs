using System.Numerics;
using Vista.Core.Scenes;
using Vista.Plugin.Session;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>Where Vista saves: shows the vistaxiv folder, changes its parent with Dalamud's folder picker, and continues to the Vista window.</summary>
internal sealed class SetupWindow : Window
{
    private const float Width = 460f;

    private readonly SceneFiles files;
    private readonly Action continued;
    private readonly FileDialogManager picker = new();
    private string? parent;

    public SetupWindow(SceneFiles files, Action continued)
        : base("Setup###vista-setup", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.files = files;
        this.continued = continued;
        RespectCloseHotkey = false;
    }

    /// <summary>Starts from the folder in use each time it opens.</summary>
    public override void OnOpen() => parent = files.Parent;

    /// <summary>Centres the window the first time it appears.</summary>
    public override void PreDraw()
        => ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    public override void Draw()
    {
        parent ??= files.Parent;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width);
        ImGui.TextUnformatted("Vista saves your scenes and presets in:");
        using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, UiColours.Accent))
            ImGui.TextUnformatted(SceneFolder.RootFor(parent));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var half = (Width - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (ImGui.Button("Change…", new Vector2(half, 0f)))
            picker.OpenFolderDialog("Choose where Vista saves", (ok, path) => { if (ok) parent = path; }, parent);
        ImGui.SameLine();
        if (ImGui.Button("Continue", new Vector2(half, 0f)) && files.Choose(parent) is null && files.Ready)
        {
            IsOpen = false;
            continued();
        }

        picker.Draw();
    }
}
