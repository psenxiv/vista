using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>A text field edited in place: Enter or clicking away applies it, Escape cancels.</summary>
internal static class TextEdit
{
    /// <summary>What the field did this frame.</summary>
    public enum Result
    {
        Editing,
        Apply,
        Cancel,
    }

    /// <summary>Draws the field <paramref name="width"/> wide, taking the keyboard when <paramref name="focus"/>.</summary>
    public static Result Draw(
        string id,
        ref string text,
        int maxLength,
        float width,
        bool focus,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None
    )
    {
        if (focus)
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(width);
        var entered = ImGui.InputText(
            id,
            ref text,
            maxLength,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | flags
        );
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            return Result.Cancel;
        return entered || ImGui.IsItemDeactivated() ? Result.Apply : Result.Editing;
    }
}
