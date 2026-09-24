using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui;

/// <summary>The first-load greeting for testers; opens once per install, records itself as seen when closed, and hands on to setting up.</summary>
internal sealed class WelcomeWindow : Window
{
    private const float Width = 420f;

    private static readonly string[] Paragraphs =
    [
        "Thank you for helping test Vista!",
        "Vista is in private beta and still a work in progress. Expect things to change, and some things to break.",
        "To learn how it works, open the User Guide with the ? at the top right of the Vista window.",
    ];

    private const string Feedback = "If you find a bug or have an idea, please reach out on Discord";

    private readonly Configuration config;
    private readonly Action continued;

    public WelcomeWindow(Configuration config, Action continued)
        : base("Welcome to Vista###vista-welcome", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.config = config;
        this.continued = continued;
        RespectCloseHotkey = false;
        IsOpen = !config.WelcomeSeen;
    }

    /// <summary>Centres the window the first time it appears.</summary>
    public override void PreDraw()
        => ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    /// <summary>Ok and the close button both land here, so either one dismisses it for good and moves on to Setup, or the Vista window once a folder is set.</summary>
    public override void OnClose()
    {
        if (config.WelcomeSeen) return;
        config.WelcomeSeen = true;
        config.Save();
        continued();
    }

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Width);
        foreach (var paragraph in Paragraphs)
        {
            ImGui.TextUnformatted(paragraph);
            ImGui.Spacing();
        }

        DrawFeedback();
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button("Ok", new Vector2(Width, 0f))) IsOpen = false;
    }

    /// <summary>The feedback line with a red heart after it, on the same line when it fits; the game font has no emoji.</summary>
    private static void DrawFeedback()
    {
        var heart = FontAwesomeIcon.Heart.ToIconString();
        float heartWidth;
        using (ImRaii.PushFont(UiBuilder.IconFont)) heartWidth = ImGui.CalcTextSize(heart).X;

        ImGui.TextUnformatted(Feedback);
        if (ImGui.CalcTextSize(Feedback).X + ImGui.GetStyle().ItemSpacing.X + heartWidth <= Width) ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red))
            ImGui.TextUnformatted(heart);
    }
}
