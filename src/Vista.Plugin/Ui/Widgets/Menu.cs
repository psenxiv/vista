using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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

    /// <summary>A menu item with <paramref name="icon"/> before its <paramref name="label"/>, highlighted while <paramref name="selected"/>, hovered and chosen as one item across the menu's width; true when chosen.</summary>
    public static bool IconItem(string id, FontAwesomeIcon icon, string label, bool selected = false)
    {
        var iconWidth = IconButton.GlyphWidth(icon);
        var gap = ImGui.GetStyle().ItemInnerSpacing.X;
        var width = iconWidth + gap + ImGui.CalcTextSize(label).X;
        // Sized to its content so the menu fits it, but hovered across the menu's width, as ImGui's own menu items are.
        var span = (ImGuiSelectableFlags)ImGuiSelectableFlagsPrivate.SpanAvailWidth;
        var chosen = ImGui.Selectable($"##{id}", selected, span, new Vector2(width, 0f));

        // A selectable's box reaches half the item spacing past its text, left and up.
        var spacing = ImGui.GetStyle().ItemSpacing;
        var at = ImGui.GetItemRectMin() + new Vector2(MathF.Floor(spacing.X * 0.5f), MathF.Floor(spacing.Y * 0.5f));
        var list = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGuiCol.Text);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            list.AddText(at, colour, icon.ToIconString());
        list.AddText(at with { X = at.X + iconWidth + gap }, colour, label);
        return chosen;
    }

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
