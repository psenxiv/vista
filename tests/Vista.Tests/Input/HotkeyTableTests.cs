using Vista.Core.Input;
using Xunit;

namespace Vista.Tests.Input;

public class HotkeyTableTests
{
    [Fact]
    public void DisplayNamesMatchWhatTheAppShowsToday()
    {
        Assert.Equal("W", HotkeyTable.FlyForward.Hotkey.DisplayName);
        Assert.Equal("A", HotkeyTable.FlyLeft.Hotkey.DisplayName);
        Assert.Equal("S", HotkeyTable.FlyBack.Hotkey.DisplayName);
        Assert.Equal("D", HotkeyTable.FlyRight.Hotkey.DisplayName);
        Assert.Equal("E", HotkeyTable.FlyUp.Hotkey.DisplayName);
        Assert.Equal("Q", HotkeyTable.FlyDown.Hotkey.DisplayName);
        Assert.Equal("Ctrl + Q", HotkeyTable.RollLeft.Hotkey.DisplayName);
        Assert.Equal("Ctrl + E", HotkeyTable.RollRight.Hotkey.DisplayName);
        Assert.Equal("Alt + R", HotkeyTable.LevelRoll.Hotkey.DisplayName);
        Assert.Equal("Shift", HotkeyTable.FlyFaster.Hotkey.DisplayName);
        Assert.Equal("Space", HotkeyTable.Play.Hotkey.DisplayName);
        Assert.Equal("Ctrl + Space", HotkeyTable.Restart.Hotkey.DisplayName);
        Assert.Equal("Backtick", HotkeyTable.AddToEnd.Hotkey.DisplayName);
        Assert.Equal("Alt + Backtick", HotkeyTable.AddAfterSelected.Hotkey.DisplayName);
        Assert.Equal("Ctrl + Backtick", HotkeyTable.OverwriteSelected.Hotkey.DisplayName);
        Assert.Equal("R", HotkeyTable.GizmoToggle.Hotkey.DisplayName);
        Assert.Equal("G", HotkeyTable.ColourByTurnSpeed.Hotkey.DisplayName);
        Assert.Equal("Delete / Backspace", HotkeyTable.DeleteSelectedPoints.Hotkey.DisplayName);
        Assert.Equal("Ctrl + Z", HotkeyTable.Undo.Hotkey.DisplayName);
        Assert.Equal("Ctrl + Y", HotkeyTable.Redo.Hotkey.DisplayName);
        Assert.Equal("Escape", HotkeyTable.RestoreGameUi.Hotkey.DisplayName);
    }

    [Fact]
    public void AnAlternateKeyIsShownWithASlashBetweenTheTwoNames()
    {
        var hotkey = new Hotkey(Key.Delete, Alternate: Key.Backspace);

        Assert.Equal("Delete / Backspace", hotkey.DisplayName);
    }

    [Fact]
    public void ModifiersJoinBeforeTheKeyInAFixedOrder()
    {
        var hotkey = new Hotkey(Key.G, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);

        Assert.Equal("Ctrl + Alt + Shift + G", hotkey.DisplayName);
    }

    [Fact]
    public void TheTooltipHelperNamesTheActionThenTheKeyInParentheses()
    {
        Assert.Equal("Play (Space)", HotkeyTable.Play.Hotkey.Tooltip("Play"));
        Assert.Equal("Restart (Ctrl + Space)", HotkeyTable.Restart.Hotkey.Tooltip("Restart"));
    }

    [Fact]
    public void EveryEntryIsFindableByItsName()
    {
        Assert.Equal(HotkeyTable.Restart, HotkeyTable.Find("Restart"));
        Assert.Null(HotkeyTable.Find("Nope"));
    }
}
