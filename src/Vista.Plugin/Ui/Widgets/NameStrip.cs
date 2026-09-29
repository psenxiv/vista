using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>A full-width band one frame high naming what a panel shows, cut to fit and scrolled while hovered; display only.</summary>
internal static class NameStrip
{
    /// <summary>Draws <paramref name="name"/> in a band across the space left on the line, keyed by <paramref name="id"/>.</summary>
    public static void Draw(string id, string name)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(ImGui.GetContentRegionAvail().X, 1f), ImGui.GetFrameHeight());
        var max = min + size;
        // Gives RowText the hover that scrolls a long name.
        ImGui.InvisibleButton(id, size);
        ImGui
            .GetWindowDrawList()
            .AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), ImGui.GetStyle().FrameRounding);
        RowText.Draw(id, name, min, max, size.X);
    }
}
