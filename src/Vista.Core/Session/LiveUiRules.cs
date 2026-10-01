namespace Vista.Core.Session;

/// <summary>What each <see cref="LiveUi"/> choice is called and does.</summary>
public static class LiveUiRules
{
    /// <summary>The heading over the choices, saying when they apply.</summary>
    public const string Heading = "When Live";

    /// <summary>The choice's name in the menu.</summary>
    public static string Label(LiveUi ui) =>
        ui switch
        {
            LiveUi.ShowAll => "Show all UI",
            LiveUi.HideGame => "Hide game UI",
            LiveUi.HideAll => "Hide all UI",
            _ => throw new ArgumentOutOfRangeException(nameof(ui), ui, null),
        };

    /// <summary>The button's tooltip: the heading and the choice in force, as "When Live: Hide game UI".</summary>
    public static string Tooltip(LiveUi ui) => $"{Heading}: {Label(ui)}";

    /// <summary>True when playing in Live hides the game's UI.</summary>
    public static bool HidesGameUi(LiveUi ui) => ui != LiveUi.ShowAll;

    /// <summary>True when Vista's windows stay up though the game's UI is hidden: only for <see cref="LiveUi.HideGame"/>, and only while Vista is what hid it.</summary>
    public static bool KeepsWindows(LiveUi ui, bool hiddenByVista) => ui == LiveUi.HideGame && hiddenByVista;

    /// <summary>The choice that matches the setting from before there were three: hiding hid everything.</summary>
    public static LiveUi FromLegacy(bool hide) => hide ? LiveUi.HideAll : LiveUi.ShowAll;
}
