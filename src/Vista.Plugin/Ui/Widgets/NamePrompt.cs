using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Vista.Core.Scenes;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>A name prompt: Ok stays disabled while the name can't be used and says why; a check returning a notice turns Ok into Replace.</summary>
internal sealed class NamePrompt(string id)
{
    /// <summary>The name field's buffer, a little past the longest name so a longer one can be typed and refused.</summary>
    private const int NameBuffer = SceneNames.MaxLength + 8;

    private readonly string popup = $"Name###vista-name-{id}";
    private bool asking;
    private bool open;
    private bool focus;
    private string heading = string.Empty;
    private string text = string.Empty;
    private (string Text, string? Refusal, string? Notice)? checkedName;
    private Func<string, (string? Refusal, string? Notice)> check = _ => (null, null);
    private Action<string> confirm = _ => { };

    /// <summary>True while the prompt is open and asking; check before acting on a key the prompt itself might be reading.</summary>
    public bool Asking => asking;

    /// <summary>Opens the prompt titled <paramref name="heading"/> with <paramref name="suggestion"/> entered; <paramref name="check"/> says why the current text is refused, or gives a notice (turning Ok into Replace) when it isn't; <paramref name="confirm"/> runs with the trimmed name when accepted.</summary>
    public void Ask(
        string heading,
        string suggestion,
        Func<string, (string? Refusal, string? Notice)> check,
        Action<string> confirm
    )
    {
        this.heading = heading;
        text = suggestion;
        this.check = check;
        this.confirm = confirm;
        checkedName = null;
        asking = true;
        open = true;
        focus = true;
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

        ImGui.TextUnformatted(heading);
        if (focus)
        {
            ImGui.SetKeyboardFocusHere();
            focus = false;
        }
        ImGui.SetNextItemWidth(Layout.DialogWidth);
        var entered = ImGui.InputText(
            "##name",
            ref text,
            NameBuffer,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll
        );

        // Checked when the text changes, not every frame, since a scene check lists the folder.
        if (checkedName is not { } cached || cached.Text != text)
        {
            var (refusal, notice) = check(text);
            checkedName = cached = (text, refusal, notice);
        }

        var (_, currentRefusal, currentNotice) = cached;
        // Always a line here, blank when the name is fine, so the buttons don't jump as you type.
        using (ImRaii.PushColor(ImGuiCol.Text, currentRefusal is not null ? UiColours.Red : UiColours.Muted()))
            ImGui.TextUnformatted(currentRefusal ?? currentNotice ?? " ");

        ImGui.BeginDisabled(currentRefusal is not null);
        var ok =
            ImGui.Button(currentNotice is not null ? "Replace" : "Ok", new Vector2(PromptDialog.ButtonWidth, 0f))
            || (entered && currentRefusal is null);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (PromptDialog.Cancelled())
        {
            asking = false;
            ImGui.CloseCurrentPopup();
        }

        if (ok && currentRefusal is null)
        {
            confirm(text.Trim());
            asking = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
