using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>What NamePrompt and DeleteConfirm share: beginning a modal, its button width, and reading Cancel.</summary>
internal static class PromptDialog
{
    /// <summary>The width of a prompt's two buttons.</summary>
    public const float ButtonWidth = 127f;

    /// <summary>Begins prompt <paramref name="popup"/>; true while it's open and has something to ask, otherwise it closes.</summary>
    public static bool Begin(string popup, bool asking)
    {
        if (!ImGui.BeginPopupModal(popup, ImGuiWindowFlags.AlwaysAutoResize))
            return false;
        if (asking)
            return true;
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return false;
    }

    /// <summary>A prompt's Cancel button; true when it or Escape is pressed.</summary>
    public static bool Cancelled() =>
        ImGui.Button("Cancel", new Vector2(ButtonWidth, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape);
}
