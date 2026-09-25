namespace Vista.Core.Tracks.Playback;

/// <summary>Maps a playback clock, the seconds since a cycle began, to a time in the shot.</summary>
public static class PlaybackClock
{
    /// <summary>One run of the shot: its length, or twice it for Ping-pong.</summary>
    public static double CycleLength(PlaybackDirection direction, double length) =>
        direction == PlaybackDirection.PingPong ? 2.0 * Math.Max(length, 0.0) : Math.Max(length, 0.0);

    /// <summary><paramref name="clock"/> wrapped into a cycle <paramref name="cycle"/> seconds long; 0 for a cycle with no length.</summary>
    public static double Wrap(double clock, double cycle) => cycle > 0.0 ? clock % cycle : 0.0;

    /// <summary>Where the camera is in the shot at <paramref name="clock"/>, which is clamped to the cycle.</summary>
    public static double ShotTime(PlaybackDirection direction, double length, double clock)
    {
        if (length <= 0.0)
            return 0.0;
        var c = Math.Clamp(clock, 0.0, CycleLength(direction, length));
        return direction switch
        {
            PlaybackDirection.Reverse => length - c,
            PlaybackDirection.PingPong => c <= length ? c : (2.0 * length) - c,
            _ => c,
        };
    }

    /// <summary>The clock that gives <paramref name="shotTime"/>, clamped to the shot; Ping-pong uses the return pass when <paramref name="onReturn"/>.</summary>
    public static double ClockFor(PlaybackDirection direction, double length, double shotTime, bool onReturn)
    {
        if (length <= 0.0)
            return 0.0;
        var t = Math.Clamp(shotTime, 0.0, length);
        return direction switch
        {
            PlaybackDirection.Reverse => length - t,
            PlaybackDirection.PingPong => onReturn ? (2.0 * length) - t : t,
            _ => t,
        };
    }

    /// <summary>True when a Ping-pong clock is past the turnaround, heading back to the start.</summary>
    public static bool OnReturnPass(PlaybackDirection direction, double length, double clock) =>
        direction == PlaybackDirection.PingPong && clock > length;
}
