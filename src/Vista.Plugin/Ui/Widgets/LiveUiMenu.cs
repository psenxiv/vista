using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Vista.Core.Session;
using Vista.Plugin.Session;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The choice of what playing in Live hides: its icons, and its heading and three items for a menu.</summary>
internal static class LiveUiMenu
{
    /// <summary>The icon for <paramref name="ui"/>: an open eye, a half-struck eye, or a struck one.</summary>
    public static FontAwesomeIcon Icon(LiveUi ui) =>
        ui switch
        {
            LiveUi.ShowAll => FontAwesomeIcon.Eye,
            LiveUi.HideGame => FontAwesomeIcon.LowVision,
            _ => FontAwesomeIcon.EyeSlash,
        };

    /// <summary>The widest the choice's button gets, whichever icon it shows.</summary>
    public static float ButtonWidth() => Enum.GetValues<LiveUi>().Max(ui => IconButton.Width(Icon(ui)));

    /// <summary>The heading, then one item per choice with the one in force highlighted; choosing one sets it.</summary>
    public static void Draw(GameSession game)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Muted()))
            ImGui.TextUnformatted(LiveUiRules.Heading);
        var current = game.LiveUi;
        foreach (var ui in Enum.GetValues<LiveUi>())
        {
            if (Menu.IconItem($"live-ui-{ui}", Icon(ui), LiveUiRules.Label(ui), ui == current))
                game.LiveUi = ui;
        }
    }
}
