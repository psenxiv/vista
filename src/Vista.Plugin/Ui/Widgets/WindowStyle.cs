using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>The styles Vista's windows push for the rest of their Draw; dispose to pop them.</summary>
internal static class WindowStyle
{
    /// <summary>An outline and an opaque background for the popups, menus, drop-downs and prompts a window opens, so they stand out over the game.</summary>
    public static IDisposable Popups()
    {
        var background = ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg] with { W = 1f };
        return new Scopes(
            ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f),
            ImRaii.PushColor(ImGuiCol.PopupBg, ImGui.ColorConvertFloat4ToU32(background))
        );
    }

    /// <summary>Vista's spacing, the popup style, and selected rows in the accent colour.</summary>
    public static IDisposable Push() =>
        new Scopes(
            ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Layout.Spacing),
            Popups(),
            ImRaii
                .PushColor(ImGuiCol.Header, UiColours.Selected())
                .Push(ImGuiCol.HeaderHovered, UiColours.SelectedHovered())
                .Push(ImGuiCol.HeaderActive, UiColours.SelectedActive())
        );

    /// <summary>Pushed scopes, popped in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            for (var i = scopes.Length - 1; i >= 0; i--)
                scopes[i].Dispose();
        }
    }
}
