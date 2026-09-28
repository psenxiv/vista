namespace Vista.Core.Input;

/// <summary>A press Vista takes: its hotkey, whether it acts now, and whether it's hidden from the game.</summary>
public readonly record struct HotkeyPress(HotkeyEntry Entry, bool Acts, bool Hidden);
