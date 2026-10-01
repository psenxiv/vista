namespace Vista.Core.Session;

/// <summary>What playing in Live hides.</summary>
public enum LiveUi
{
    /// <summary>Nothing.</summary>
    ShowAll,

    /// <summary>The game's UI; Vista's windows stay.</summary>
    HideGame,

    /// <summary>The game's UI and Vista's windows with it.</summary>
    HideAll,
}
