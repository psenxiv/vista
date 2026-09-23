using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The first-load greeting for testers; opens once per install and records itself as seen when closed.</summary>
internal sealed class WelcomeWindow : Window
{
    private const float Width = 420f;

    private static readonly string[] Paragraphs =
    [
        "Thank you for helping test Vista.",
        "Vista is in private beta and still a work in progress. Expect things to change, and some things to break.",
        "To learn how it works, open the User Guide with the ? at the top right of the Vista window.",
        "If you find a bug or have an idea, let me know on Discord.",
    ];

    private readonly Configuration config;

    public WelcomeWindow(Configuration config)
        : base("Welcome to Vista###vista-welcome", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.config = config;
        RespectCloseHotkey = false;
        IsOpen = !config.WelcomeSeen;
    }

    /// <summary>Centres the window the first time it appears.</summary>
    public override void PreDraw()
        => ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    /// <summary>Ok and the close button both land here, so either one dismisses it for good.</summary>
    public override void OnClose()
    {
        if (config.WelcomeSeen) return;
        config.WelcomeSeen = true;
        config.Save();
    }

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width);
        foreach (var paragraph in Paragraphs)
        {
            ImGui.TextUnformatted(paragraph);
            ImGui.Spacing();
        }

        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button("Ok", new Vector2(Width, 0f))) IsOpen = false;
    }
}
