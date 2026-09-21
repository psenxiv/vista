namespace CinematicCam.Core;

/// <summary>Builds a track's timing keys from control points, legs and holds, without exposing tangents.</summary>
public static class TrackEditing
{
    /// <summary>Seconds a leg takes when a point is appended without one being set explicitly.</summary>
    public const float DefaultLegSeconds = 5f;

    /// <summary>A track with no points, no keys, and <see cref="PlaybackMode.Once"/>.</summary>
    public static Track Empty(AimMode aim = AimMode.AimKeys)
        => new(Array.Empty<ControlPoint>(), Array.Empty<TimingKey>(), aim, PlaybackMode.Once);

    /// <summary>Appends a point, giving it a key one <see cref="DefaultLegSeconds"/> after the previous last key.</summary>
    public static Track Append(Track track, ControlPoint point)
    {
        var points = new List<ControlPoint>(track.Points) { point };
        var index = track.Points.Count;
        var time = track.Timing.Count == 0 ? 0f : track.Timing[^1].Time + DefaultLegSeconds;

        var timing = new List<TimingKey>(track.Timing) { new(time, index, TangentMode.Auto, 0f, 0f) };
        return track with { Points = points, Timing = timing };
    }

    /// <summary>The time from point i-1's last key to point i's first key.</summary>
    public static float LegSeconds(Track track, int index)
    {
        ValidateLegIndex(track, index);
        var keys = track.Timing;
        return keys[FirstKeyIndex(keys, index)].Time - keys[LastKeyIndex(keys, index - 1)].Time;
    }

    /// <summary>Sets leg i, shifting every key at or after point i's first key by the difference.</summary>
    public static Track SetLeg(Track track, int index, float seconds)
    {
        ValidateLegIndex(track, index);
        if (!float.IsFinite(seconds) || seconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "leg seconds must be > 0");

        var keys = track.Timing;
        var prevLastIndex = LastKeyIndex(keys, index - 1);
        var currFirstIndex = FirstKeyIndex(keys, index);
        var diff = seconds - (keys[currFirstIndex].Time - keys[prevLastIndex].Time);

        var result = new List<TimingKey>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
            result.Add(i >= currFirstIndex ? keys[i] with { Time = keys[i].Time + diff } : keys[i]);

        return track with { Timing = result };
    }

    /// <summary>The time between point i's two keys, 0 when it has no second key.</summary>
    public static float HoldSeconds(Track track, int index)
    {
        ValidatePointIndex(track, index, "hold");
        var keys = track.Timing;
        var firstIndex = FirstKeyIndex(keys, index);
        var lastIndex = LastKeyIndex(keys, index);
        return lastIndex == firstIndex ? 0f : keys[lastIndex].Time - keys[firstIndex].Time;
    }

    /// <summary>Sets hold i, adding, resizing or (at 0) removing point i's second key; later keys shift with it.</summary>
    public static Track SetHold(Track track, int index, float seconds)
    {
        ValidatePointIndex(track, index, "hold");
        if (!float.IsFinite(seconds) || seconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "hold seconds must be >= 0");

        var keys = track.Timing;
        var firstIndex = FirstKeyIndex(keys, index);
        var lastIndex = LastKeyIndex(keys, index);
        var hasHold = lastIndex != firstIndex;
        var currentHold = hasHold ? keys[lastIndex].Time - keys[firstIndex].Time : 0f;
        var diff = seconds - currentHold;

        var result = new List<TimingKey>(keys.Count + (hasHold ? 0 : 1));
        for (var i = 0; i < keys.Count; i++)
        {
            if (hasHold && i == lastIndex)
            {
                if (seconds == 0f) continue; // drop the hold key
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            if (i > lastIndex)
            {
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            result.Add(keys[i]);
            if (!hasHold && i == firstIndex && seconds > 0f)
                result.Add(new TimingKey(keys[i].Time + seconds, index, TangentMode.Auto, 0f, 0f));
        }

        return track with { Timing = result };
    }

    /// <summary>Sets the playback mode; never touches points or keys.</summary>
    public static Track SetPlayback(Track track, PlaybackMode mode)
        => track with { Playback = mode };

    private static void ValidateLegIndex(Track track, int index)
    {
        var n = track.Points.Count;
        if (index < 1 || index > n - 1)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"leg index must be 1..{n - 1} for a {n}-point track");
    }

    private static void ValidatePointIndex(Track track, int index, string what)
    {
        var n = track.Points.Count;
        if (index < 0 || index > n - 1)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{what} index must be 0..{n - 1} for a {n}-point track");
    }

    private static int FirstKeyIndex(IReadOnlyList<TimingKey> keys, int point)
    {
        for (var i = 0; i < keys.Count; i++)
            if (keys[i].Position == point) return i;

        throw new InvalidOperationException($"no timing key found for point {point}");
    }

    private static int LastKeyIndex(IReadOnlyList<TimingKey> keys, int point)
    {
        var last = -1;
        for (var i = 0; i < keys.Count; i++)
            if (keys[i].Position == point) last = i;

        if (last < 0)
            throw new InvalidOperationException($"no timing key found for point {point}");

        return last;
    }
}
