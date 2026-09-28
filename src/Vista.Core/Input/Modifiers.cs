namespace Vista.Core.Input;

/// <summary>The modifier keys a hotkey can require, held down alongside its key.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
}
