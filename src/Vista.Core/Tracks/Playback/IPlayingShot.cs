namespace Vista.Core.Tracks.Playback;

/// <summary>A playing shot as its scrub bar sees it: its entries laid end to end, where its head is, and dragging the head.</summary>
public interface IPlayingShot
{
    /// <summary>The shot laid end to end, or null when nothing is playing.</summary>
    PlaylistTimeline? Timeline { get; }

    /// <summary>Seconds through the shot; 0 when nothing is playing.</summary>
    double Head { get; }

    /// <summary>The playing entry's index among the shot's segments; 0 when nothing is playing.</summary>
    int EntryIndex { get; }

    /// <summary>True between <see cref="BeginScrub"/> and <see cref="EndScrub"/>.</summary>
    bool Scrubbing { get; }

    /// <summary>Starts dragging the head; the shot holds until <see cref="EndScrub"/>.</summary>
    void BeginScrub();

    /// <summary>Moves the head to <paramref name="time"/> through the shot, clamped to it.</summary>
    void ScrubTo(double time);

    /// <summary>Stops dragging the head; the shot carries on as it was.</summary>
    void EndScrub();
}
