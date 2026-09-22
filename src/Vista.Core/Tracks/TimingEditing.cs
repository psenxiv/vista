namespace Vista.Core.Tracks;

/// <summary>Edits timing through its keys: their modes, handles, holds and drags.</summary>
public static class TimingEditing
{
    /// <summary>Sets key <paramref name="key"/>'s sides to Auto, Linear or Flat: a point key's arrival and, without a hold, its departure; a hold end's departure.</summary>
    public static Track SetKeyMode(Track track, int key, TangentMode mode)
    {
        var (point, role) = (TrackEditing.PointOf(track, key), TrackEditing.RoleOf(track, key));
        if (mode == TangentMode.Manual) throw new ArgumentException("a key's mode can't be set to Manual directly");

        var timing = track.Timing[point];
        if (role == KeyRole.Point) timing = timing with { InMode = mode, InTangent = 0f };
        if (HasDeparture(track, point, role)) timing = timing with { OutMode = mode, OutTangent = 0f };
        return TrackEditing.WithTiming(track, point, timing);
    }

    /// <summary>Sets Manual slopes, in stored units, on the key's sides given; a null side, or one the key doesn't own, is left alone. Negative slopes become 0.</summary>
    public static Track SetHandles(Track track, int key, float? inSlope, float? outSlope)
    {
        var (point, role) = (TrackEditing.PointOf(track, key), TrackEditing.RoleOf(track, key));
        var timing = track.Timing[point];
        if (role == KeyRole.Point && inSlope is { } i) timing = timing with { InMode = TangentMode.Manual, InTangent = Slope(i) };
        if (HasDeparture(track, point, role) && outSlope is { } o) timing = timing with { OutMode = TangentMode.Manual, OutTangent = Slope(o) };
        return TrackEditing.WithTiming(track, point, timing);
    }

    /// <summary>Breaks or unifies the handles of key <paramref name="key"/>'s point.</summary>
    public static Track SetBroken(Track track, int key, bool broken)
    {
        var point = TrackEditing.PointOf(track, key);
        return TrackEditing.WithTiming(track, point, track.Timing[point] with { Broken = broken });
    }

    /// <summary>True when a span lies on <paramref name="side"/> of key <paramref name="key"/> and it isn't a hold.</summary>
    public static bool HasHandle(Track track, int key, KeySide side)
    {
        var (point, role) = (TrackEditing.PointOf(track, key), TrackEditing.RoleOf(track, key));
        return side == KeySide.In
            ? key > 0 && role != KeyRole.HoldEnd
            : key < TrackEditing.KeyCount(track) - 1 && HasDeparture(track, point, role);
    }

    /// <summary>Removes the hold a hold end closes. A point's key has no hold to remove.</summary>
    public static Track RemoveHold(Track track, int key)
    {
        if (TrackEditing.RoleOf(track, key) != KeyRole.HoldEnd) throw new ArgumentException("only a hold end can remove its hold");
        return TrackEditing.SetHold(track, TrackEditing.PointOf(track, key), 0f);
    }

    /// <summary>Drags key <paramref name="key"/> towards <paramref name="time"/>, pinning the legs whose time changes; <paramref name="evaluator"/> is the track's.</summary>
    public static Track MoveKey(Track track, TrackEvaluator evaluator, int key, float time)
    {
        var (point, role) = (TrackEditing.PointOf(track, key), TrackEditing.RoleOf(track, key));
        if (key == 0 || float.IsNaN(time)) return track;

        var keys = evaluator.Keys;
        if (role == KeyRole.HoldEnd)
            return TrackEditing.SetHold(track, point, Math.Clamp(time - keys[key - 1].Time, TrackEditing.MinKeyGap, TrackEditing.MaxSeconds));

        var start = keys[key - 1].Time;
        var length = evaluator.LegLength(point);
        var (shortest, longest) = LegRange(length);
        var lower = start + shortest;
        var upper = start + longest;

        var holds = track.Timing[point].Hold > 0f;
        var hasNext = point < track.Points.Count - 1;
        var next = holds || hasNext ? keys[key + 1].Time : 0f;
        if (holds)
        {
            lower = MathF.Max(lower, next - TrackEditing.MaxSeconds);
            upper = MathF.Min(upper, next - TrackEditing.MinKeyGap);
        }
        else if (hasNext)
        {
            var (nextShortest, nextLongest) = LegRange(evaluator.LegLength(point + 1));
            lower = MathF.Max(lower, next - nextLongest);
            upper = MathF.Min(upper, next - nextShortest);
        }

        if (lower > upper) return track;
        var target = Math.Clamp(time, lower, upper);
        if (target == keys[key].Time) return track;

        var result = TrackEditing.SetLegSpeed(track, point, length / (target - start));
        if (holds) return TrackEditing.SetHold(result, point, next - target);
        return hasNext ? TrackEditing.SetLegSpeed(result, point + 1, evaluator.LegLength(point + 1) / (next - target)) : result;
    }

    /// <summary>The shortest and longest a leg of <paramref name="length"/> can take within the speed and leg ranges.</summary>
    private static (float Shortest, float Longest) LegRange(float length)
        => (TimingCompiler.LegDuration(length, TrackEditing.MaxSpeed), TimingCompiler.LegDuration(length, TrackEditing.MinSpeed));

    /// <summary>True when the key sets its point's departure side: a hold end, or a point key without a hold.</summary>
    private static bool HasDeparture(Track track, int point, KeyRole role) => role == KeyRole.HoldEnd || track.Timing[point].Hold <= 0f;

    private static float Slope(float value) => float.IsFinite(value) ? MathF.Max(value, 0f) : 0f;
}
