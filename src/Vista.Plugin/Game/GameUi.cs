using FFXIVClientStructs.FFXIV.Client.UI;

namespace Vista.Plugin.Game;

/// <summary>Hides and restores the game UI, the same as the game's own UI toggle.</summary>
internal static unsafe class GameUi
{
    private static bool hiddenByUs;

    /// <summary>True while the game UI is hidden because we hid it.</summary>
    public static bool HiddenByUs => hiddenByUs;

    /// <summary>Hides the game UI if it is showing, and remembers that we did.</summary>
    public static void Hide() =>
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var module = RaptureAtkModule.Instance();
            if (module == null || !module->IsUiVisible)
                return;
            module->IsUiVisible = false;
            hiddenByUs = true;
            Plugin.Log.Debug("[ui] hidden; visible now {Visible}", module->IsUiVisible);
        });

    /// <summary>Shows the game UI again, only if we were the ones who hid it.</summary>
    public static void Restore() =>
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var module = RaptureAtkModule.Instance();
            var visible = module != null && module->IsUiVisible;
            if (!hiddenByUs)
                return;
            hiddenByUs = false;
            if (module != null && !visible)
                module->IsUiVisible = true;
            Plugin.Log.Debug(
                "[ui] restored; was visible {Was}, visible now {Now}",
                visible,
                module != null && module->IsUiVisible
            );
        });
}
