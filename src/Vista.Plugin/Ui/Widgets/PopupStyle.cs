using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>An outline and an opaque background for the popups, menus, drop-downs and prompts a window opens, so they stand out over the game.</summary>
internal static class PopupStyle
{
    /// <summary>Pushes the popup style for the rest of a window's Draw; dispose to pop it.</summary>
    public static IDisposable Push()
    {
        var background = ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg] with { W = 1f };
        return new Both(
            ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
            ImRaii.PushColor(ImGuiCol.PopupBg, ImGui.ColorConvertFloat4ToU32(background)));
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}
