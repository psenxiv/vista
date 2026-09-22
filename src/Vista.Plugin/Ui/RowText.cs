using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui;

/// <summary>Text drawn inside a list row's selectable.</summary>
internal static class RowText
{
    /// <summary>Draws <paramref name="text"/> inside the item just drawn: centred on its height, inset by twice the frame padding and clipped to the row.</summary>
    public static void Draw(string text)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var padding = ImGui.GetStyle().FramePadding.X * 2f;
        var at = new Vector2(min.X + padding, min.Y + ((max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f));
        var list = ImGui.GetWindowDrawList();
        list.PushClipRect(min, max with { X = max.X - padding }, true);
        list.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), text);
        list.PopClipRect();
    }
}
