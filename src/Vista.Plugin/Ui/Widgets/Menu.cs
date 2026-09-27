using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Entries in Vista's menus.</summary>
internal static class Menu
{
    /// <summary>A menu item with no tick, greyed out unless <paramref name="enabled"/>, showing <paramref name="shortcut"/> at its right; true when chosen.</summary>
    public static bool Item(string label, bool enabled = true, string shortcut = "") =>
        ImGui.MenuItem(label, shortcut, false, enabled);

    /// <summary>A menu item ticked while <paramref name="on"/>, which choosing flips; greyed out unless <paramref name="enabled"/>; true when chosen.</summary>
    public static bool Check(string label, ref bool on, bool enabled = true, string shortcut = "") =>
        ImGui.MenuItem(label, shortcut, ref on, enabled);

    /// <summary>A labelled slider in a menu, <paramref name="width"/> wide, greyed out unless <paramref name="enabled"/>; true when moved or typed.</summary>
    public static bool Slider(
        string label,
        ref float value,
        float min,
        float max,
        string format,
        float width,
        bool enabled = true
    )
    {
        using var disabled = ImRaii.Disabled(!enabled);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(width);
        return ImGui.SliderFloat($"##{label}", ref value, min, max, format, ImGuiSliderFlags.AlwaysClamp);
    }
}
