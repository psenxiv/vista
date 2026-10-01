using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>A full-width band one frame high naming what a panel shows, cut to fit and scrolled while hovered; clicking it opens the list it was chosen from.</summary>
internal static class NameStrip
{
    /// <summary>Draws <paramref name="name"/> in a band across the space left on the line, keyed by <paramref name="id"/>; true when clicked, which only an <paramref name="enabled"/> band can be, with <paramref name="tooltip"/> saying what the click does.</summary>
    public static bool Draw(string id, string name, string tooltip, bool enabled)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(ImGui.GetContentRegionAvail().X, 1f), ImGui.GetFrameHeight());
        var max = min + size;
        // Also gives RowText the hover that scrolls a long name.
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var fill =
            !enabled ? ImGuiCol.FrameBg
            : ImGui.IsItemActive() ? ImGuiCol.FrameBgActive
            : ImGui.IsItemHovered() ? ImGuiCol.FrameBgHovered
            : ImGuiCol.FrameBg;
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(fill), ImGui.GetStyle().FrameRounding);
        RowText.Draw(id, name, min, max, size.X);
        if (enabled)
        {
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            Tooltip.OnHover(tooltip);
        }

        return clicked;
    }
}
