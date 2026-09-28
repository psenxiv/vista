namespace Vista.Core.Input;

/// <summary>A hotkey Vista binds, named for the Plugin to look up and the Guide's {key:Name} tag to resolve.</summary>
public sealed record HotkeyEntry(string Name, Hotkey Hotkey);
