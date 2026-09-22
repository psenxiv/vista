using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Vista.Plugin.Game;

/// <summary>Reads keys from their physical state, since Dalamud releases non-modifier keys in ImGui each frame, and hides them from the game.</summary>
internal static unsafe class PhysicalKeys
{
    /// <summary>True while the key is held and the game window is active.</summary>
    public static bool IsDown(VirtualKey key)
    {
        var framework = GameFramework.Instance();
        if (framework == null || framework->WindowInactive) return false;
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    /// <summary>Clears a key from the game's buffer; it stays cleared while held.</summary>
    public static void Hide(VirtualKey key) => Plugin.KeyState[key] = false;

    /// <summary>True while typing in game chat or a plugin text field.</summary>
    public static bool IsTyping()
    {
        var module = RaptureAtkModule.Instance();
        return (module != null && module->IsTextInputActive()) || ImGui.GetIO().WantTextInput;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
