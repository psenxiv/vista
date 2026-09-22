using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>A number field bordered in a colour and named by its tooltip, shown even while disabled.</summary>
internal static class BorderedField
{
    /// <summary>Draws the drag field at <paramref name="width"/>; true while its value changes.</summary>
    public static bool Draw(string id, string name, uint? border, ref float value, float speed, string format, float width)
    {
        ImGui.SetNextItemWidth(width);
        bool changed;
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f, border is not null))
        using (ImRaii.PushColor(ImGuiCol.Border, border ?? 0u, border is not null))
            changed = ImGui.DragFloat($"##{id}", ref value, speed, 0f, 0f, format);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(name);
        return changed;
    }
}
