using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>"Delete name?", followed by "This can't be undone." unless it can be.</summary>
internal sealed class DeleteConfirm(string id)
{
    private readonly string popup = $"Delete###vista-delete-{id}";
    private bool asking;
    private bool open;
    private string message = string.Empty;
    private Action confirm = () => { };

    /// <summary>True while the confirmation is open and asking; check before acting on a key it might be reading.</summary>
    public bool Asking => asking;

    /// <summary>Opens the confirmation for <paramref name="name"/>, warning unless <paramref name="undoable"/>; <paramref name="confirm"/> runs when Delete is pressed.</summary>
    public void Ask(string name, bool undoable, Action confirm)
    {
        message = undoable ? $"Delete {name}?" : $"Delete {name}? This can't be undone.";
        this.confirm = confirm;
        asking = true;
        open = true;
    }

    /// <summary>Opens and draws the popup; call once a frame from outside any other popup's scope.</summary>
    public void Draw()
    {
        if (open)
        {
            ImGui.OpenPopup(popup);
            open = false;
        }

        if (!PromptDialog.Begin(popup, asking))
            return;

        ImGui.TextUnformatted(message);
        if (ImGui.Button("Delete", new Vector2(PromptDialog.ButtonWidth, 0f)))
        {
            confirm();
            asking = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (PromptDialog.Cancelled())
        {
            asking = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
