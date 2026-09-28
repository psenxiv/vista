using Dalamud.Game.ClientState.Keys;
using Vista.Core.Input;

namespace Vista.Plugin.Game;

/// <summary>Maps a bound hotkey's Core key to Dalamud's VirtualKey; the only file that names one for a bound hotkey.</summary>
internal static class HotkeyKeys
{
    /// <summary>Ctrl's VirtualKey, for reading a hotkey's Ctrl modifier from physical key state.</summary>
    public const VirtualKey Ctrl = VirtualKey.CONTROL;

    /// <summary>Alt's VirtualKey, for reading a hotkey's Alt modifier from physical key state.</summary>
    public const VirtualKey Alt = VirtualKey.MENU;

    /// <summary>The VirtualKey for a Core key Vista binds.</summary>
    public static VirtualKey Virtual(Key key) =>
        key switch
        {
            Key.W => VirtualKey.W,
            Key.A => VirtualKey.A,
            Key.S => VirtualKey.S,
            Key.D => VirtualKey.D,
            Key.E => VirtualKey.E,
            Key.Q => VirtualKey.Q,
            Key.R => VirtualKey.R,
            Key.G => VirtualKey.G,
            Key.Z => VirtualKey.Z,
            Key.Y => VirtualKey.Y,
            Key.Space => VirtualKey.SPACE,
            Key.Backtick => VirtualKey.OEM_3,
            Key.Delete => VirtualKey.DELETE,
            Key.Backspace => VirtualKey.BACK,
            Key.Escape => VirtualKey.ESCAPE,
            Key.Shift => VirtualKey.SHIFT,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no VirtualKey mapped for this key"),
        };
}
