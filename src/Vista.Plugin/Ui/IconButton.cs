using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace CinematicCam.Plugin.Ui;

/// <summary>Icon buttons with a tooltip, and their width for right-aligning them.</summary>
internal static class IconButton
{
    /// <summary>An icon button with a tooltip naming it, shown even while disabled.</summary>
    public static bool Draw(string id, FontAwesomeIcon icon, string tooltip)
    {
        var pressed = ImGuiComponents.IconButton(id, icon);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return pressed;
    }

    /// <summary>The width Dalamud gives an icon button: the glyph plus frame padding on both sides.</summary>
    public static float Width(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X + (ImGui.GetStyle().FramePadding.X * 2f);
    }
}
