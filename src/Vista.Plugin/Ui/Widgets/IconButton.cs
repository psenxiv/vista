using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Frameless icon buttons with a tooltip, their toggle, window and row-action variants, icons drawn as text, the not-found warning, and icon and row widths for laying them out.</summary>
internal static class IconButton
{
    // The danger button hovered last frame, so its icon can be red while hovered; older frames are stale.
    private static (uint Id, int Frame)? dangerHovered;

    /// <summary>A frameless icon button with a tooltip, shown even while disabled; <paramref name="danger"/> turns the icon red on hover.</summary>
    public static bool Draw(
        string id,
        FontAwesomeIcon icon,
        string tooltip,
        uint? iconColour = null,
        bool danger = false
    )
    {
        var red =
            danger
            && dangerHovered is { } last
            && last.Id == ImGui.GetID(id)
            && last.Frame == ImGui.GetFrameCount() - 1;
        var colour = red ? UiColours.Red : iconColour;
        bool pressed;
        using (Frameless())
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            pressed = ImGuiComponents.IconButton(id, icon);
        if (danger && ImGui.IsItemHovered())
            dangerHovered = (ImGui.GetID(id), ImGui.GetFrameCount());

        Tooltip.OnHover(tooltip);
        return pressed;
    }

    /// <summary>A toggle: the icon in the accent colour when on, dimmed when off.</summary>
    public static bool Toggle(string id, FontAwesomeIcon icon, bool on, string tooltip) =>
        Draw(id, icon, tooltip, on ? UiColours.Accent : UiColours.Dim());

    /// <summary>Opens or closes <paramref name="window"/>; the icon is in the accent colour while it's open, and plain rather than dimmed while it's closed.</summary>
    public static void WindowToggle(string id, FontAwesomeIcon icon, string tooltip, Window window)
    {
        if (Draw(id, icon, tooltip, window.IsOpen ? UiColours.Accent : null))
            window.Toggle();
    }

    /// <summary>A row's action, drawn only while the row is hovered; otherwise its space stays empty.</summary>
    public static bool RowAction(string id, FontAwesomeIcon icon, string tooltip, bool rowHovered, bool danger = false)
    {
        if (rowHovered)
            return Draw(id, icon, tooltip, danger: danger);
        ImGui.Dummy(new Vector2(Width(icon), ImGui.GetFrameHeight()));
        return false;
    }

    /// <summary>True when the mouse is over the rectangle and the window or one of its children is hovered.</summary>
    public static bool RowHovered(Vector2 min, Vector2 max) =>
        ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
        && ImGui.IsMouseHoveringRect(min, max, false);

    /// <summary>True when the mouse is over the row from <paramref name="min"/> to the window's right edge, <paramref name="bottom"/> high, and the window or one of its children is hovered.</summary>
    public static bool RowHovered(Vector2 min, float bottom) =>
        RowHovered(min, new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, bottom));

    /// <summary>The tooltip on a warning that a watched character can't be found.</summary>
    public const string NotFoundTooltip = "Not found nearby: using recorded aim.";

    /// <summary>The tooltip on a warning that a followed character can't be found.</summary>
    public const string FollowNotFoundTooltip = "Not found nearby.";

    /// <summary>A red warning icon saying the character can't be found, its tooltip shown even while disabled.</summary>
    public static void TargetNotFound(string tooltip)
    {
        ImGui.AlignTextToFramePadding();
        Glyph(WarningIcon, UiColours.Red);
        Tooltip.OnHover(tooltip);
    }

    /// <summary>The not-found warning's icon.</summary>
    public const FontAwesomeIcon WarningIcon = FontAwesomeIcon.ExclamationTriangle;

    /// <summary>An icon drawn as text in the icon font, in <paramref name="colour"/> when given.</summary>
    public static void Glyph(FontAwesomeIcon icon, uint? colour = null)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, colour ?? 0u, colour is not null))
            ImGui.TextUnformatted(icon.ToIconString());
    }

    /// <summary>The width of an icon drawn as text.</summary>
    public static float GlyphWidth(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X;
    }

    /// <summary>The width Dalamud gives an icon button: the glyph plus frame padding on both sides.</summary>
    public static float Width(FontAwesomeIcon icon) => GlyphWidth(icon) + (ImGui.GetStyle().FramePadding.X * 2f);

    /// <summary>The width of <paramref name="icons"/>' buttons side by side, with the item spacing between them.</summary>
    public static float RowWidth(params ReadOnlySpan<FontAwesomeIcon> icons)
    {
        Span<float> widths = stackalloc float[icons.Length];
        for (var i = 0; i < icons.Length; i++)
            widths[i] = Width(icons[i]);
        return RowWidth(widths);
    }

    /// <summary>The width of items <paramref name="widths"/> wide side by side, with the item spacing between them.</summary>
    public static float RowWidth(params ReadOnlySpan<float> widths)
    {
        if (widths.IsEmpty)
            return 0f;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var total = widths[0];
        for (var i = 1; i < widths.Length; i++)
            total = total + gap + widths[i];
        return total;
    }

    /// <summary>No button background until hovered, then a soft rounded highlight.</summary>
    private static FramelessScope Frameless()
    {
        var hovered = ImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.35f);
        var active = ImGui.GetColorU32(ImGuiCol.ButtonActive, 0.5f);
        var colours = ImRaii
            .PushColor(ImGuiCol.Button, 0u)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);
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
