namespace Vista.Plugin.Session;

/// <summary>A scrub one control began, which only that control ends, so a window closing mid-drag doesn't end another's.</summary>
internal sealed class Scrubber(GameSession game)
{
    /// <summary>True from <see cref="Begin"/> until this control's scrub ends.</summary>
    public bool Active { get; private set; }

    /// <summary>Starts dragging the scrub head; this control's scrub is active once the session is scrubbing.</summary>
    public void Begin()
    {
        game.State.Transport.BeginScrub();
        Active = game.State.Transport.Scrubbing;
    }

    /// <summary>Ends the scrub if this control began it, flying the free-cam to the scrub head in Edit.</summary>
    public void End()
    {
        if (!Active)
            return;
        Active = false;
        game.FinishScrub();
    }
}
