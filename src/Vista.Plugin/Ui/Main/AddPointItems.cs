using Vista.Core.Input;
using Vista.Plugin.Session;
using Vista.Plugin.Ui.Widgets;
using static Vista.Plugin.Ui.Widgets.Refusal;

namespace Vista.Plugin.Ui.Main;

/// <summary>The ways to add a point at the camera, as menu items: at the end, after the selected point, or over it.</summary>
internal static class AddPointItems
{
    /// <summary>Draws the three items into the open menu or popup, acting on the one clicked.</summary>
    public static void Draw(GameSession game)
    {
        var selected = game.State.Selection.Point is not null;
        if (Menu.Item("Add to end", shortcut: HotkeyTable.AddToEnd.Hotkey.DisplayName))
            Report(game.AddToEnd());
        if (Menu.Item("Add after selected", selected, HotkeyTable.AddAfterSelected.Hotkey.DisplayName))
            Report(game.AddAfterSelected());
        if (Menu.Item("Overwrite selected", selected, HotkeyTable.OverwriteSelected.Hotkey.DisplayName))
            Report(game.OverwriteSelected());
    }
}
