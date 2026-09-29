using Dalamud.Bindings.ImGui;
using Vista.Core.Display;
using Vista.Core.Tracks;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>What the scrub bars share: their smaller grab and zooming with the wheel.</summary>
internal static class ScrubBar
{
    /// <summary>A scrub bar's grab, as a share of the style's usual slider grab.</summary>
    public const float GrabScale = 0.5f;

    /// <summary>The wheel over the scrub bar just drawn zooms <paramref name="zoom"/> around the mouse, unless the bar is being dragged.</summary>
    public static void Zoom<TKey>(ViewZoom<TKey> zoom, TimingView view, float total, bool dragging)
        where TKey : IEquatable<TKey>
    {
        if (!ImGui.IsItemHovered())
            return;
        ImGuiP.SetItemUsingMouseWheel();
        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f || dragging)
            return;
        var along = Fraction.Between(ImGui.GetMousePos().X, ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().X, 0.5f);
        zoom.Zoom(view.TimeAt(along), wheel, total);
    }
}
