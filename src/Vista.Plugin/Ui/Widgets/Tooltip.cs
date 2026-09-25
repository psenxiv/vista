using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Tooltips on the item just drawn.</summary>
internal static class Tooltip
{
    /// <summary>Shows <paramref name="text"/> while the item just drawn is hovered, even while it's disabled.</summary>
    public static void OnHover(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
    }
}
