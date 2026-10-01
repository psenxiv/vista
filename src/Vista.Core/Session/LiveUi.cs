namespace Vista.Core.Session;

/// <summary>What playing in Live hides. Saved by number, so new choices go at the end.</summary>
public enum LiveUi
{
    /// <summary>Nothing.</summary>
    ShowAll,

    /// <summary>The game's UI; Vista's windows stay.</summary>
    HideGame,

    /// <summary>The game's UI and Vista's windows with it.</summary>
    HideAll,
}
