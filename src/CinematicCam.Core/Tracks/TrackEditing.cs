namespace CinematicCam.Core.Tracks;

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
            throw new ArgumentOutOfRangeException(null, "leg seconds must be > 0");

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
            throw new ArgumentOutOfRangeException(null, "hold seconds must be >= 0");

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

    /// <summary>Inserts a point after point <paramref name="index"/>, splitting that leg by path length; after the last point it appends.</summary>
    public static Track InsertAfter(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "insert");
        RequireKeyPerPoint(track);
        if (index == track.Points.Count - 1) return Append(track, point);

        var points = new List<ControlPoint>(track.Points);
        points.Insert(index + 1, point);

        var table = new ArcLengthTable(points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index + 1), TrackEvaluator.MinTimingLength);

        var keys = track.Timing;
        var start = keys[LastKeyIndex(keys, index)].Time;
        var leg = keys[FirstKeyIndex(keys, index + 1)].Time - start;
        var inserted = new TimingKey(start + (leg * before / (before + after)), index + 1, TangentMode.Auto, 0f, 0f);

        var timing = new List<TimingKey>(keys.Count + 1);
        timing.AddRange(keys.Where(k => k.Position <= index));
        timing.Add(inserted);
        timing.AddRange(keys.Where(k => k.Position > index).Select(k => k with { Position = k.Position + 1 }));

        return track with { Points = points, Timing = timing };
    }

    /// <summary>Removes point <paramref name="index"/>: a middle point's legs and hold merge, an end point's leg and hold go.</summary>
    public static Track Delete(Track track, int index)
    {
        ValidatePointIndex(track, index, "delete");
        RequireKeyPerPoint(track);
        if (track.Points.Count == 1)
            return track with { Points = Array.Empty<ControlPoint>(), Timing = Array.Empty<TimingKey>() };

        var keys = track.Timing;
        var shift = index == 0 ? keys[FirstKeyIndex(keys, 1)].Time - keys[0].Time : 0f;

        var points = new List<ControlPoint>(track.Points);
        points.RemoveAt(index);

        var timing = keys
            .Where(k => k.Position != index)
            .Select(k => k with { Time = k.Time - shift, Position = k.Position > index ? k.Position - 1 : k.Position })
            .ToList();

        return track with { Points = points, Timing = timing };
    }

    /// <summary>Moves point <paramref name="from"/> to position <paramref name="to"/>; holds travel with their point, leg times stay in their slots.</summary>
    public static Track Move(Track track, int from, int to)
    {
        ValidatePointIndex(track, from, "move");
        ValidatePointIndex(track, to, "move");
        RequireKeyPerPoint(track);
        if (from == to) return track;

        var n = track.Points.Count;
        var keys = track.Timing;
        var legs = new float[n];
        for (var i = 1; i < n; i++) legs[i] = LegSeconds(track, i);

        var order = Enumerable.Range(0, n).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);

        var timing = new List<TimingKey>(keys.Count);
        var time = keys[0].Time;
        for (var slot = 0; slot < n; slot++)
        {
            var firstIndex = FirstKeyIndex(keys, order[slot]);
            var lastIndex = LastKeyIndex(keys, order[slot]);
            if (slot > 0) time += legs[slot];

            timing.Add(keys[firstIndex] with { Time = time, Position = slot });
            if (lastIndex == firstIndex) continue;

            time += keys[lastIndex].Time - keys[firstIndex].Time;
            timing.Add(keys[lastIndex] with { Time = time, Position = slot });
        }

        return track with { Points = order.Select(i => track.Points[i]).ToList(), Timing = timing };
    }

    /// <summary>Replaces point <paramref name="index"/>, keeping every timing key.</summary>
    public static Track Replace(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "replace");
        if (Equals(track.Points[index], point)) return track;

        var points = new List<ControlPoint>(track.Points) { [index] = point };
        return track with { Points = points };
    }

    private static void ValidateLegIndex(Track track, int index)
    {
        var n = track.Points.Count;
        if (n < 2)
            throw new ArgumentOutOfRangeException(null, "this track has no legs");
        if (index < 1 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"leg index must be 1..{n - 1} for a {n}-point track");
    }

    private static void ValidatePointIndex(Track track, int index, string what)
    {
        var n = track.Points.Count;
        if (n == 0)
            throw new ArgumentOutOfRangeException(null, "this track has no points");
        if (index < 0 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"{what} index must be 0..{n - 1} for a {n}-point track");
    }

    private static int FirstKeyIndex(IReadOnlyList<TimingKey> keys, int point)
    {
        for (var i = 0; i < keys.Count; i++)
            if (keys[i].Position == point) return i;

        throw new ArgumentException($"point {point} has no timing key");
    }

    private static int LastKeyIndex(IReadOnlyList<TimingKey> keys, int point)
    {
        var last = -1;
        for (var i = 0; i < keys.Count; i++)
            if (keys[i].Position == point) last = i;

        if (last < 0)
            throw new ArgumentException($"point {point} has no timing key");

        return last;
    }

    private static void RequireKeyPerPoint(Track track)
    {
        var counts = new int[track.Points.Count];
        foreach (var key in track.Timing)
        {
            var position = (int)key.Position;
            if (key.Position != position || position < 0 || position >= counts.Length)
                throw new ArgumentException("timing keys between points are not supported yet");
            counts[position]++;
        }

        if (counts.Any(c => c is < 1 or > 2))
            throw new ArgumentException("timing keys between points are not supported yet");
    }
}
