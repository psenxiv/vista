using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui;

/// <summary>Frameless icon buttons with a tooltip, their toggle and row-action variants, the not-found warning, and their width for right-aligning them.</summary>
internal static class IconButton
{
    // The danger button hovered last frame, so its icon can be red while hovered; older frames are stale.
    private static (uint Id, int Frame)? dangerHovered;

    /// <summary>A frameless icon button with a tooltip, shown even while disabled; <paramref name="danger"/> turns the icon red on hover.</summary>
    public static bool Draw(string id, FontAwesomeIcon icon, string tooltip, uint? iconColour = null, bool danger = false)
    {
        var red = danger && dangerHovered is { } last && last.Id == ImGui.GetID(id) && last.Frame == ImGui.GetFrameCount() - 1;
        var colour = red ? UiColours.Red : iconColour;
        bool pressed;
        using (Frameless())
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            pressed = ImGuiComponents.IconButton(id, icon);
        if (danger && ImGui.IsItemHovered()) dangerHovered = (ImGui.GetID(id), ImGui.GetFrameCount());

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return pressed;
    }

    /// <summary>A toggle: the icon in the accent colour when on, dimmed when off.</summary>
    public static bool Toggle(string id, FontAwesomeIcon icon, bool on, string tooltip)
        => Draw(id, icon, tooltip, on ? UiColours.Accent : UiColours.Dim());

    /// <summary>A row's action, drawn only while the row is hovered; otherwise its space stays empty.</summary>
    public static bool RowAction(string id, FontAwesomeIcon icon, string tooltip, bool rowHovered, bool danger = false)
    {
        if (rowHovered) return Draw(id, icon, tooltip, danger: danger);
        ImGui.Dummy(new Vector2(Width(icon), ImGui.GetFrameHeight()));
        return false;
    }

    /// <summary>True when the mouse is over the rectangle and the window or one of its children is hovered.</summary>
    public static bool RowHovered(Vector2 min, Vector2 max)
        => ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) && ImGui.IsMouseHoveringRect(min, max, false);

    /// <summary>The tooltip on a warning that a watched character can't be found.</summary>
    public const string NotFoundTooltip = "Not found nearby: using recorded aim";

    /// <summary>The tooltip on a warning that a followed character can't be found.</summary>
    public const string FollowNotFoundTooltip = "Not found nearby: using the last position";

    /// <summary>A red warning icon saying the character can't be found, its tooltip shown even while disabled.</summary>
    public static void TargetNotFound(string tooltip)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColours.Red))
            ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
    }

    /// <summary>The warning icon's width.</summary>
    public static float WarningWidth()
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(FontAwesomeIcon.ExclamationTriangle.ToIconString()).X;
    }

    /// <summary>The width Dalamud gives an icon button: the glyph plus frame padding on both sides.</summary>
    public static float Width(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X + (ImGui.GetStyle().FramePadding.X * 2f);
    }

    /// <summary>No button background until hovered, then a soft rounded highlight.</summary>
    private static FramelessScope Frameless()
    {
        var hovered = ImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.35f);
        var active = ImGui.GetColorU32(ImGuiCol.ButtonActive, 0.5f);
        var colours = ImRaii.PushColor(ImGuiCol.Button, 0u).Push(ImGuiCol.ButtonHovered, hovered).Push(ImGuiCol.ButtonActive, active);
        return new FramelessScope(colours, ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 4f));
    }

    /// <summary>The frameless colours and rounding, popped together.</summary>
    private readonly struct FramelessScope(ImRaii.ColorDisposable colours, ImRaii.StyleDisposable style) : IDisposable
    {
        public void Dispose()
        {
            style.Dispose();
            colours.Dispose();
        }
    }
}
