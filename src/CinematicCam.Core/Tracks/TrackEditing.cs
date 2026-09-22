namespace CinematicCam.Core.Tracks;

/// <summary>Builds a track's timing keys from control points, legs and holds, without exposing tangents.</summary>
public static class TrackEditing
{
    /// <summary>Seconds a leg takes when a point is appended without one being set explicitly.</summary>
    public const float DefaultLegSeconds = 5f;

    /// <summary>Keys stay at least this many seconds apart.</summary>
    public const float MinKeyGap = 0.05f;

    /// <summary>The shortest leg, in seconds.</summary>
    public const float MinLegSeconds = 0.1f;

    /// <summary>The longest leg or hold, in seconds.</summary>
    public const float MaxSeconds = 600f;

    /// <summary>A track with no points, no keys, and <see cref="PlaybackMode.Once"/>.</summary>
    public static Track Empty(AimMode aim = AimMode.AimKeys)
        => new(Array.Empty<ControlPoint>(), Array.Empty<TimingKey>(), aim, PlaybackMode.Once);

    /// <summary>Appends a point, giving it a key one <see cref="DefaultLegSeconds"/> after the previous last key.</summary>
    public static Track Append(Track track, ControlPoint point)
    {
        var points = new List<ControlPoint>(track.Points) { point };
        var index = track.Points.Count;
        var time = track.Timing.Count == 0 ? 0f : track.Timing[^1].Time + DefaultLegSeconds;

        var timing = new List<TimingKey>(track.Timing) { new(time, index) };
        return track with { Points = points, Timing = timing };
    }

    /// <summary>The time from point i-1's last key to point i's first key.</summary>
    public static float LegSeconds(Track track, int index)
    {
        ValidateLegIndex(track, index);
        var keys = track.Timing;
        return keys[FirstKeyIndex(keys, index)].Time - keys[LastKeyIndex(keys, index - 1)].Time;
    }

    /// <summary>Sets leg i, shifting every key at or after point i's first key by the difference and spreading its inner keys.</summary>
    public static Track SetLeg(Track track, int index, float seconds)
    {
        ValidateLegIndex(track, index);
        RequireValidKeys(track);
        if (!float.IsFinite(seconds) || seconds <= 0f)
            throw new ArgumentOutOfRangeException(null, "leg seconds must be > 0");

        seconds = MathF.Max(seconds, MinLegFor(track, index));
        var keys = track.Timing;
        var prevLastIndex = LastKeyIndex(keys, index - 1);
        var currFirstIndex = FirstKeyIndex(keys, index);
        var start = keys[prevLastIndex].Time;
        var oldLeg = keys[currFirstIndex].Time - start;
        var diff = seconds - oldLeg;

        var result = new List<TimingKey>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > prevLastIndex && i < currFirstIndex)
                result.Add(keys[i] with { Time = start + ((keys[i].Time - start) * seconds / oldLeg) });
            else
                result.Add(i >= currFirstIndex ? keys[i] with { Time = keys[i].Time + diff } : keys[i]);
        }

        return track with { Timing = result };
    }

    /// <summary>The shortest leg <paramref name="leg"/> can be while its inner keys stay <see cref="MinKeyGap"/> apart.</summary>
    public static float MinLegFor(Track track, int leg)
    {
        var start = LegStartKey(track, leg);
        var end = LegEndKey(track, leg);
        if (end - start < 2) return MinLegSeconds;

        var keys = track.Timing;
        var smallest = float.MaxValue;
        for (var i = start; i < end; i++) smallest = MathF.Min(smallest, keys[i + 1].Time - keys[i].Time);
        var length = keys[end].Time - keys[start].Time;
        return MathF.Max(MinLegSeconds, length * MinKeyGap / smallest);
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
                if (seconds == 0f) continue;
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            if (i > lastIndex)
            {
                result.Add(keys[i] with { Time = keys[i].Time + diff });
                continue;
            }

            if (i == firstIndex && hasHold && seconds == 0f)
            {
                result.Add(keys[i] with { OutMode = keys[lastIndex].OutMode, OutTangent = keys[lastIndex].OutTangent });
                continue;
            }

            if (i == firstIndex && !hasHold && seconds > 0f)
            {
                result.Add(keys[i] with { OutMode = TangentMode.Auto, OutTangent = 0f });
                result.Add(new TimingKey(keys[i].Time + seconds, index, OutMode: keys[i].OutMode, OutTangent: keys[i].OutTangent));
                continue;
            }

            result.Add(keys[i]);
        }

        return track with { Timing = result };
    }

    /// <summary>The time point <paramref name="index"/> is reached: its first key.</summary>
    public static float PointSeconds(Track track, int index)
    {
        ValidatePointIndex(track, index, "time");
        return track.Timing[FirstKeyIndex(track.Timing, index)].Time;
    }

    /// <summary>What timing key <paramref name="key"/> is.</summary>
    public static KeyRole RoleOf(Track track, int key)
    {
        var position = track.Timing[key].Position;
        if (position != MathF.Floor(position)) return KeyRole.Inner;
        return key > 0 && track.Timing[key - 1].Position == position ? KeyRole.HoldEnd : KeyRole.Point;
    }

    /// <summary>Index of point <paramref name="point"/>'s first key.</summary>
    public static int PointKey(Track track, int point)
    {
        ValidatePointIndex(track, point, "point");
        return FirstKeyIndex(track.Timing, point);
    }

    /// <summary>Index of the key leg <paramref name="leg"/> leaves from: the last key of the point before it.</summary>
    public static int LegStartKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return LastKeyIndex(track.Timing, leg - 1);
    }

    /// <summary>Index of the key leg <paramref name="leg"/> arrives at: its point's first key.</summary>
    public static int LegEndKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return FirstKeyIndex(track.Timing, leg);
    }

    /// <summary>The leg whose time span holds <paramref name="time"/>, or null in a hold or outside the shot.</summary>
    public static int? LegAt(Track track, float time)
    {
        for (var leg = 1; leg < track.Points.Count; leg++)
        {
            if (time >= track.Timing[LegStartKey(track, leg)].Time && time <= track.Timing[LegEndKey(track, leg)].Time) return leg;
        }

        return null;
    }

    /// <summary>Sets the playback mode; never touches points or keys.</summary>
    public static Track SetPlayback(Track track, PlaybackMode mode)
        => track with { Playback = mode };

    /// <summary>Inserts a point after point <paramref name="index"/>, splitting that leg by path length; after the last point it appends.</summary>
    public static Track InsertAfter(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "insert");
        RequireValidKeys(track);
        if (index == track.Points.Count - 1) return Append(track, point);

        var points = new List<ControlPoint>(track.Points);
        points.Insert(index + 1, point);

        var table = new ArcLengthTable(points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index + 1), TrackEvaluator.MinTimingLength);
        var share = before / (before + after);

        var keys = track.Timing;
        var startIndex = LastKeyIndex(keys, index);
        var endIndex = FirstKeyIndex(keys, index + 1);
        var start = keys[startIndex].Time;
        var time = start + ((keys[endIndex].Time - start) * share);

        var timing = new List<TimingKey>(keys.Count + 1);
        timing.AddRange(keys.Take(startIndex + 1));
        var second = new List<TimingKey>();
        for (var i = startIndex + 1; i < endIndex; i++)
        {
            var key = keys[i];
            if (MathF.Abs(key.Time - time) < MinKeyGap) continue;
            var fraction = key.Position - index;
            if (key.Time < time) timing.Add(key with { Position = index + InsideLeg(fraction / share) });
            else second.Add(key with { Position = index + 1 + InsideLeg((fraction - share) / (1f - share)) });
        }

        timing.Add(new TimingKey(time, index + 1));
        timing.AddRange(second);
        timing.AddRange(keys.Skip(endIndex).Select(k => k with { Position = k.Position + 1 }));
        return track with { Points = points, Timing = timing };
    }

    /// <summary>A fraction of a leg kept strictly inside it.</summary>
    private static float InsideLeg(float fraction) => Math.Clamp(fraction, 0.001f, 0.999f);

    /// <summary>Removes point <paramref name="index"/>: a middle point's legs and hold merge, an end point's leg and hold go.</summary>
    public static Track Delete(Track track, int index)
    {
        ValidatePointIndex(track, index, "delete");
        RequireValidKeys(track);
        if (track.Points.Count == 1)
            return track with { Points = Array.Empty<ControlPoint>(), Timing = Array.Empty<TimingKey>() };

        var keys = track.Timing;
        var n = track.Points.Count;
        var points = new List<ControlPoint>(track.Points);
        points.RemoveAt(index);

        if (index == 0)
        {
            var shift = keys[FirstKeyIndex(keys, 1)].Time;
            return track with { Points = points, Timing = keys.Where(k => k.Position >= 1f).Select(k => k with { Time = k.Time - shift, Position = k.Position - 1 }).ToList() };
        }

        if (index == n - 1)
            return track with { Points = points, Timing = keys.Where(k => k.Position <= n - 2).ToList() };

        var table = new ArcLengthTable(track.Points.Select(p => p.Position).ToArray());
        var before = MathF.Max(table.SegmentLength(index - 1), TrackEvaluator.MinTimingLength);
        var after = MathF.Max(table.SegmentLength(index), TrackEvaluator.MinTimingLength);
        var total = before + after;

        var timing = new List<TimingKey>(keys.Count);
        foreach (var key in keys)
        {
            if (key.Position <= index - 1) timing.Add(key);
            else if (key.Position < index) timing.Add(key with { Position = index - 1 + ((key.Position - (index - 1)) * before / total) });
            else if (key.Position == index) continue;
            else if (key.Position < index + 1) timing.Add(key with { Position = index - 1 + ((before + ((key.Position - index) * after)) / total) });
            else timing.Add(key with { Position = key.Position - 1 });
        }

        return track with { Points = points, Timing = timing };
    }

    /// <summary>Moves point <paramref name="from"/> to position <paramref name="to"/>; holds travel with their point, leg times stay in their slots.</summary>
    public static Track Move(Track track, int from, int to)
    {
        ValidatePointIndex(track, from, "move");
        ValidatePointIndex(track, to, "move");
        RequireValidKeys(track);
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
            var moved = order[slot];
            var movedFirst = keys[FirstKeyIndex(keys, moved)];
            var movedLastIndex = LastKeyIndex(keys, moved);
            var hasHold = movedLastIndex != FirstKeyIndex(keys, moved);
            var slotFirst = keys[FirstKeyIndex(keys, slot)];
            var slotLast = keys[LastKeyIndex(keys, slot)];

            if (slot > 0)
            {
                var originalStart = keys[LastKeyIndex(keys, slot - 1)].Time;
                foreach (var inner in keys.Where(k => k.Position > slot - 1 && k.Position < slot))
                    timing.Add(inner with { Time = time + (inner.Time - originalStart) });
                time += legs[slot];
            }

            var arrival = movedFirst with { Time = time, Position = slot, InMode = slotFirst.InMode, InTangent = slotFirst.InTangent, Broken = slotFirst.Broken };
            if (!hasHold)
            {
                timing.Add(arrival with { OutMode = slotLast.OutMode, OutTangent = slotLast.OutTangent });
                continue;
            }

            timing.Add(arrival);
            time += keys[movedLastIndex].Time - movedFirst.Time;
            timing.Add(keys[movedLastIndex] with { Time = time, Position = slot, OutMode = slotLast.OutMode, OutTangent = slotLast.OutTangent });
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

    /// <summary>Checks each point has one or two keys of its own and every other key lies between the first and last point.</summary>
    private static void RequireValidKeys(Track track)
    {
        var counts = new int[track.Points.Count];
        foreach (var key in track.Timing)
        {
            var whole = MathF.Floor(key.Position);
            if (key.Position != whole)
            {
                if (key.Position <= 0f || key.Position >= track.Points.Count - 1)
                    throw new ArgumentException("a timing key lies outside the track");
                continue;
            }

            var point = (int)whole;
            if (point < 0 || point >= counts.Length) throw new ArgumentException("a timing key lies outside the track");
            counts[point]++;
        }

        if (counts.Any(c => c is < 1 or > 2)) throw new ArgumentException("every point needs one or two timing keys");
    }
}
