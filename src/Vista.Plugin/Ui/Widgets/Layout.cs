using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The sizes Vista's windows share, right-aligning items, and placing and sizing windows.</summary>
internal static class Layout
{
    /// <summary>The item spacing Vista's windows draw with.</summary>
    public static readonly Vector2 Spacing = new(8f, 7f);

    /// <summary>A number field's width.</summary>
    public const float FieldWidth = 70f;

    /// <summary>A dialog's width: its character list, name field and full-width button.</summary>
    public const float DialogWidth = 260f;

    /// <summary>Moves the cursor so an item of <paramref name="width"/> ends at the right edge.</summary>
    public static void RightAlign(float width) =>
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - width));

    /// <summary>Centres the next window on the screen as it appears; call from its PreDraw.</summary>
    public static void CentreOnAppearing() =>
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    /// <summary>A window's size limits: at least <paramref name="minimum"/>, with no maximum.</summary>
    public static WindowSizeConstraints AtLeast(Vector2 minimum) =>
        new() { MinimumSize = minimum, MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
}
