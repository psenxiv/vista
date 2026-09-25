namespace Vista.Core.Tracks.Timing;

/// <summary>What a timing key's menu offers: removing its hold, and breaking or unifying its handles from the side given.</summary>
public readonly record struct KeyActions(bool RemoveHold, bool BreakHandles, bool UnifyHandles, KeySide UnifyFrom)
{
    /// <summary>True when the menu offers anything.</summary>
    public bool Any => RemoveHold || BreakHandles || UnifyHandles;
}
