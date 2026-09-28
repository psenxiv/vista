namespace Vista.Plugin.Ui.Widgets;

/// <summary>The feedback form the top row and the Help menu open.</summary>
internal static class Feedback
{
    /// <summary>The button's tooltip and the menu item's name.</summary>
    public const string Label = "Give feedback";

    private const string Url = "https://forms.gle/p9hAJvQZT7qZLN5T7";

    /// <summary>Opens the form in the default browser.</summary>
    public static void Open() => Dalamud.Utility.Util.OpenLink(Url);
}
