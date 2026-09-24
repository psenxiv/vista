namespace Vista.Core.Session;

/// <summary>How <see cref="SessionState.Edit"/> changed the mode.</summary>
public enum EditOutcome
{
    Unchanged,
    FromGame,
    FromLive,
}
