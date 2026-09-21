using FFXIVClientStructs.FFXIV.Client.UI;

namespace CinematicCam.Plugin.Game;

/// <summary>Hides and restores the game UI, the same as the game's own UI toggle.</summary>
internal static unsafe class GameUi
{
    private static bool hiddenByUs;

    /// <summary>Hides the game UI if it is showing, and remembers that we did.</summary>
    public static void Hide() => Plugin.Framework.RunOnFrameworkThread(() =>
    {
        var module = RaptureAtkModule.Instance();
        if (module == null || !module->IsUiVisible) return;
        module->IsUiVisible = false;
        hiddenByUs = true;
    });

    /// <summary>Shows the game UI again, only if we were the ones who hid it.</summary>
    public static void Restore() => Plugin.Framework.RunOnFrameworkThread(() =>
    {
        if (!hiddenByUs) return;
        hiddenByUs = false;
        var module = RaptureAtkModule.Instance();
        if (module != null && !module->IsUiVisible) module->IsUiVisible = true;
    });
}
