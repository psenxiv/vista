namespace CinematicCam.Core.Tracks;

/// <summary>Edits timing keys directly: their modes, times, handles, and keys between points.</summary>
public static class TimingEditing
{
    /// <summary>How close an inner key may come to its neighbours' places, in control-point units.</summary>
    private const float PositionGap = 1e-4f;

    /// <summary>Sets both sides of key <paramref name="key"/> to Auto, Linear or Flat.</summary>
    public static Track SetKeyMode(Track track, int key, TangentMode mode)
    {
        ValidateKey(track, key);
        if (mode == TangentMode.Manual) throw new ArgumentException("a key's mode can't be set to Manual directly");
        var k = track.Timing[key];
        if (k.InMode == mode && k.OutMode == mode) return track;
        return WithKey(track, key, k with { InMode = mode, OutMode = mode, InTangent = 0f, OutTangent = 0f });
    }

    /// <summary>Moves key <paramref name="key"/> towards <paramref name="time"/>, and an inner key towards <paramref name="position"/>, clamped to keep order and spacing.</summary>
    public static Track MoveKey(Track track, int key, float time, float position)
    {
        ValidateKey(track, key);
        if (key == 0) return track;

        var keys = track.Timing;
        if (TrackEditing.RoleOf(track, key) == KeyRole.Inner)
        {
            var prev = keys[key - 1];
            var next = keys[key + 1];
            var t = Math.Clamp(time, prev.Time + TrackEditing.MinKeyGap, next.Time - TrackEditing.MinKeyGap);
            var low = prev.Position + PositionGap;
            var high = next.Position - PositionGap;
            var p = low <= high ? Math.Clamp(position, low, high) : keys[key].Position;
            return WithKey(track, key, keys[key] with { Time = t, Position = p });
        }

        var before = PreviousAnchor(track, key);
        var after = NextAnchor(track, key);
        var lower = keys[before].Time + MinStretch(track, before, key);
        var upper = after < 0 ? float.MaxValue : keys[after].Time - MinStretch(track, key, after);
        upper = MathF.Min(upper, keys[before].Time + TrackEditing.MaxSeconds);
        if (after >= 0) lower = MathF.Max(lower, keys[after].Time - TrackEditing.MaxSeconds);
        if (lower > upper) return track;

        var target = Math.Clamp(time, lower, upper);
        var old = keys[key].Time;
        if (target == old) return track;

        var timing = keys.ToList();
        var start = keys[before].Time;
        for (var i = before + 1; i < key; i++)
            timing[i] = keys[i] with { Time = start + ((keys[i].Time - start) * (target - start) / (old - start)) };
        timing[key] = keys[key] with { Time = target };
        if (after >= 0)
        {
            var end = keys[after].Time;
            for (var i = key + 1; i < after; i++)
                timing[i] = keys[i] with { Time = target + ((keys[i].Time - old) * (end - target) / (end - old)) };
        }
        else
        {
            for (var i = key + 1; i < keys.Count; i++) timing[i] = keys[i] with { Time = keys[i].Time + (target - old) };
        }

        return track with { Timing = timing };
    }

    /// <summary>Adds an inner key at <paramref name="time"/> and <paramref name="position"/> with unbroken Manual handles at <paramref name="slope"/>, in stored units.</summary>
    public static (Track Track, int Key) AddInnerKey(Track track, float time, float position, float slope)
    {
        if (TrackEditing.LegAt(track, time) is not { } leg) throw new ArgumentException("keys can only be added inside a leg");

        var keys = track.Timing;
        var index = 0;
        while (index < keys.Count && keys[index].Time <= time) index++;
        var prev = keys[index - 1];
        var next = keys[index];
        if (time - prev.Time < TrackEditing.MinKeyGap || next.Time - time < TrackEditing.MinKeyGap)
            throw new ArgumentException("too close to another key");

        var low = MathF.Max(prev.Position, leg - 1) + PositionGap;
        var high = MathF.Min(next.Position, leg) - PositionGap;
        if (low > high) throw new ArgumentException("no room for a key here");

        var s = float.IsFinite(slope) ? MathF.Max(slope, 0f) : 0f;
        var timing = keys.ToList();
        timing.Insert(index, new TimingKey(time, Math.Clamp(position, low, high), TangentMode.Manual, TangentMode.Manual, s, s));
        return (track with { Timing = timing }, index);
    }

    /// <summary>Deletes an inner key, or removes the hold a hold end closes. A point's key can't be deleted.</summary>
    public static Track DeleteKey(Track track, int key)
    {
        ValidateKey(track, key);
        switch (TrackEditing.RoleOf(track, key))
        {
            case KeyRole.Inner:
                var timing = track.Timing.ToList();
                timing.RemoveAt(key);
                return track with { Timing = timing };
            case KeyRole.HoldEnd:
                return TrackEditing.SetHold(track, (int)track.Timing[key].Position, 0f);
            default:
                throw new ArgumentException("a point's key goes with its point");
        }
    }

    /// <summary>Sets Manual slopes, in stored units, on the sides given; a null side is left alone. Negative slopes become 0.</summary>
    public static Track SetHandles(Track track, int key, float? inSlope, float? outSlope)
    {
        ValidateKey(track, key);
        var k = track.Timing[key];
        if (inSlope is { } i) k = k with { InMode = TangentMode.Manual, InTangent = Slope(i) };
        if (outSlope is { } o) k = k with { OutMode = TangentMode.Manual, OutTangent = Slope(o) };
        return k == track.Timing[key] ? track : WithKey(track, key, k);
    }

    /// <summary>Breaks or unifies key <paramref name="key"/>'s handles.</summary>
    public static Track SetBroken(Track track, int key, bool broken)
    {
        ValidateKey(track, key);
        var k = track.Timing[key];
        return k.Broken == broken ? track : WithKey(track, key, k with { Broken = broken });
    }

    /// <summary>True when a span lies on <paramref name="side"/> of key <paramref name="key"/> and it isn't a hold.</summary>
    public static bool HasHandle(Track track, int key, KeySide side)
    {
        ValidateKey(track, key);
        var keys = track.Timing;
        var neighbour = side == KeySide.In ? key - 1 : key + 1;
        return neighbour >= 0 && neighbour < keys.Count && keys[neighbour].Position != keys[key].Position;
    }

    private static float Slope(float value) => float.IsFinite(value) ? MathF.Max(value, 0f) : 0f;

    /// <summary>The nearest point key or hold end before <paramref name="key"/>.</summary>
    private static int PreviousAnchor(Track track, int key)
    {
        var i = key - 1;
        while (TrackEditing.RoleOf(track, i) == KeyRole.Inner) i--;
        return i;
    }

    /// <summary>The nearest point key or hold end after <paramref name="key"/>, or -1 for the last key.</summary>
    private static int NextAnchor(Track track, int key)
    {
        for (var i = key + 1; i < track.Timing.Count; i++)
        {
            if (TrackEditing.RoleOf(track, i) != KeyRole.Inner) return i;
        }

        return -1;
    }

    /// <summary>The shortest the stretch between two anchors may get: a hold's key gap, or a leg's minimum given its inner keys.</summary>
    private static float MinStretch(Track track, int from, int to)
    {
        var keys = track.Timing;
        if (keys[from].Position == keys[to].Position) return TrackEditing.MinKeyGap;
        if (to - from < 2) return TrackEditing.MinLegSeconds;

        var smallest = float.MaxValue;
        for (var i = from; i < to; i++) smallest = MathF.Min(smallest, keys[i + 1].Time - keys[i].Time);
        return MathF.Max(TrackEditing.MinLegSeconds, (keys[to].Time - keys[from].Time) * TrackEditing.MinKeyGap / smallest);
    }

    private static Track WithKey(Track track, int key, TimingKey value)
    {
        var timing = track.Timing.ToList();
        timing[key] = value;
        return track with { Timing = timing };
    }

    private static void ValidateKey(Track track, int key)
    {
        if (key < 0 || key >= track.Timing.Count)
            throw new ArgumentOutOfRangeException(nameof(key), $"key index must be 0..{track.Timing.Count - 1}");
    }
}
