namespace Vista.Core.Session;

/// <summary>What <see cref="SessionState.Play"/>, <see cref="SessionState.Restart"/> or <see cref="SessionState.Cue"/> did.</summary>
public enum PlayOutcome
{
    Refused,
    ReHid,
    Resumed,
    Started,
    StartedFromGame,
    Cued,
    CuedFromGame,
    Previewed,
}
