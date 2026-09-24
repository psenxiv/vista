using System.Numerics;
using Vista.Core.Display;
using Vista.Core.Editing;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Text drawn inside a list row's selectable.</summary>
internal static class RowText
{
    // The row whose name is scrolling, the frame it was last drawn hovered, and when its hover began.
    private static Guid? hovered;
    private static int hoveredFrame;
    private static double hoveredSince;

    /// <summary>Draws <paramref name="text"/> inside the item just drawn, row <paramref name="row"/>: centred on its height, inset by twice the frame padding, clipped to its first <paramref name="width"/> pixels, cut with an ellipsis, and scrolled while hovered.</summary>
    public static void Draw(Guid row, string text, float width)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var padding = ImGui.GetStyle().FramePadding.X * 2f;
        var right = MathF.Min(max.X, min.X + width) - padding;
        var room = right - (min.X + padding);
        var overflow = ImGui.CalcTextSize(text).X - room;

        var shift = 0f;
        var shown = text;
        if (overflow > 0f && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var frame = ImGui.GetFrameCount();
            if (hovered != row || hoveredFrame < frame - 1) hoveredSince = ImGui.GetTime();
            hovered = row;
            hoveredFrame = frame;
            shift = RowFit.Scroll(overflow, ImGui.GetTime() - hoveredSince);
        }
        else if (overflow > 0f)
        {
            shown = RowFit.Ellipsis(text, room, s => ImGui.CalcTextSize(s).X);
        }

        var at = new Vector2(min.X + padding - shift, min.Y + ((max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f));
        var list = ImGui.GetWindowDrawList();
        list.PushClipRect(min with { X = min.X + padding }, max with { X = right }, true);
        list.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), shown);
        list.PopClipRect();
    }
}
