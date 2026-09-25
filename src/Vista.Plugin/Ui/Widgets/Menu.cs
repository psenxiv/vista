using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Entries in Vista's menus.</summary>
internal static class Menu
{
    /// <summary>A menu item with no tick, greyed out unless <paramref name="enabled"/>, showing <paramref name="shortcut"/> at its right; true when chosen.</summary>
    public static bool Item(string label, bool enabled = true, string shortcut = "") =>
        ImGui.MenuItem(label, shortcut, false, enabled);
}
