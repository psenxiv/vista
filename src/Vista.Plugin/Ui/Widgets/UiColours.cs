using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The Vista window's colours, as ImGui ABGR.</summary>
internal static class UiColours
{
    /// <summary>Selection and toggles that are on.</summary>
    public const uint Accent = 0xFFF0A040;

    /// <summary>Loop counts.</summary>
    public const uint Amber = 0xFF40C0FF;

    /// <summary>LIVE, destructive hover and the switchboard's Program.</summary>
    public const uint Red = 0xFF4050E8;

    /// <summary>The switchboard's Next.</summary>
    public const uint Green = 0xFF60D060;

    /// <summary>The share of full opacity that off, greyed-out and unplayable things are drawn at.</summary>
    public const float DimAlpha = 0.4f;

    /// <summary>Selected rows, the playing part of a scrub bar and a Cut that's ready: the accent at 45%.</summary>
    public static uint Selected() => AccentAt(0.45f);

    /// <summary>A hovered row: the accent at 30%.</summary>
    public static uint SelectedHovered() => AccentAt(0.30f);

    /// <summary>A row being clicked: the accent at 55%.</summary>
    public static uint SelectedActive() => AccentAt(0.55f);

    /// <summary>Off and greyed-out icons: the text colour at 40%.</summary>
    public static uint Dim() => Text(DimAlpha);

    /// <summary>Section headers: the text colour at 60%.</summary>
    public static uint Muted() => Text(0.6f);

    /// <summary>Dividers and faint outlines: the text colour at 15%.</summary>
    public static uint Faint() => Text(0.15f);

    /// <summary>A flat value cell while its row is hovered: the frame colour at 40%.</summary>
    public static uint FrameHint()
    {
        var frame = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        return ImGui.ColorConvertFloat4ToU32(frame with { W = frame.W * 0.4f });
    }

    /// <summary>The accent with its alpha set to <paramref name="alpha"/>.</summary>
    private static uint AccentAt(float alpha) =>
        ImGui.ColorConvertFloat4ToU32(ImGui.ColorConvertU32ToFloat4(Accent) with { W = alpha });

    /// <summary>The text colour with its alpha scaled, leaving style alpha to disabled drawing so it applies once.</summary>
    private static uint Text(float alpha)
    {
        var text = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        return ImGui.ColorConvertFloat4ToU32(text with { W = text.W * alpha });
    }
}
