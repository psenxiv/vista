namespace Vista.Core.Tracks;

/// <summary>Edits a track's points and timing: the track speed, pinned legs and holds.</summary>
public static class TrackEditing
{
    /// <summary>A new track's speed, in yalms per second.</summary>
    public const float DefaultSpeed = 2f;

    /// <summary>The slowest track or leg speed, in yalms per second.</summary>
    public const float MinSpeed = 0.01f;

    /// <summary>The fastest track or leg speed, in yalms per second.</summary>
    public const float MaxSpeed = 100f;

    /// <summary>The shortest leg, in seconds.</summary>
    public const float MinLegSeconds = 0.1f;

    /// <summary>The longest leg or hold, in seconds.</summary>
    public const float MaxSeconds = 600f;

    /// <summary>The longest shot, in seconds.</summary>
    public const float MaxShotSeconds = 3600f;

    /// <summary>Keys stay at least this many seconds apart.</summary>
    public const float MinKeyGap = 0.05f;

    private const int DurationSteps = 60;

    /// <summary>A track with no points at the default speed, playing <see cref="PlaybackMode.Once"/>.</summary>
    public static Track Empty(AimMode aim = AimMode.AimKeys)
        => new([], [], DefaultSpeed, aim, PlaybackMode.Once);

    /// <summary>How many timing keys the track compiles to.</summary>
    public static int KeyCount(Track track) => track.Points.Count + track.Timing.Count(t => t.Hold > 0f);

    /// <summary>What timing key <paramref name="key"/> is.</summary>
    public static KeyRole RoleOf(Track track, int key) => Locate(track, key).Role;

    /// <summary>The point timing key <paramref name="key"/> belongs to.</summary>
    public static int PointOf(Track track, int key) => Locate(track, key).Point;

    /// <summary>Index of point <paramref name="point"/>'s key.</summary>
    public static int PointKey(Track track, int point)
    {
        ValidatePointIndex(track, point, "point");
        var key = 0;
        for (var p = 0; p < point; p++) key += track.Timing[p].Hold > 0f ? 2 : 1;
        return key;
    }

    /// <summary>Index of the key leg <paramref name="leg"/> leaves from: the point before it, or its hold end.</summary>
    public static int LegStartKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return PointKey(track, leg - 1) + (track.Timing[leg - 1].Hold > 0f ? 1 : 0);
    }

    /// <summary>Index of the key leg <paramref name="leg"/> arrives at.</summary>
    public static int LegEndKey(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return PointKey(track, leg);
    }

    /// <summary>Each leg's length as timing measures it, indexed by leg; index 0 is 0.</summary>
    public static float[] LegLengths(Track track)
    {
        var lengths = new float[track.Points.Count];
        if (lengths.Length < 2) return lengths;

        var table = new ArcLengthTable(track.Points.Select(p => p.Position).ToArray());
        for (var leg = 1; leg < lengths.Length; leg++)
            lengths[leg] = MathF.Max(table.SegmentLength(leg - 1), TrackEvaluator.MinTimingLength);
        return lengths;
    }

    /// <summary>True when leg <paramref name="leg"/> has its own speed.</summary>
    public static bool IsPinned(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return track.Timing[leg].LegSpeed is not null;
    }

    /// <summary>True when no leg follows the track speed, including a track with fewer than two points.</summary>
    public static bool AllPinned(Track track)
    {
        for (var leg = 1; leg < track.Points.Count; leg++)
        {
            if (track.Timing[leg].LegSpeed is null) return false;
        }

        return true;
    }

    /// <summary>The speed leg <paramref name="leg"/> is set to: its pinned speed, or the track's.</summary>
    public static float LegSpeed(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return track.Timing[leg].LegSpeed ?? track.Speed;
    }

    /// <summary>Point <paramref name="point"/>'s hold, in seconds.</summary>
    public static float HoldSeconds(Track track, int point)
    {
        ValidatePointIndex(track, point, "hold");
        return track.Timing[point].Hold;
    }

    /// <summary>Appends a point whose leg follows the track speed.</summary>
    public static Track Append(Track track, ControlPoint point)
        => track with { Points = [.. track.Points, point], Timing = [.. track.Timing, new PointTiming()] };

    /// <summary>Inserts a point after point <paramref name="index"/>, both halves keeping the split leg's speed and pin; after the last point it appends.</summary>
    public static Track InsertAfter(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "insert");
        if (index == track.Points.Count - 1) return Append(track, point);

        var points = track.Points.ToList();
        points.Insert(index + 1, point);
        var timing = track.Timing.ToList();
        timing.Insert(index + 1, new PointTiming(LegSpeed: track.Timing[index + 1].LegSpeed));

        var before = LegLengths(track);
        var after = LegLengths(track with { Points = points });
        timing[index] = ScaleOut(timing[index], before[index + 1] / after[index + 1]);
        timing[index + 2] = ScaleIn(timing[index + 2], before[index + 1] / after[index + 2]);
        return track with { Points = points, Timing = timing };
    }

    /// <summary>Removes point <paramref name="index"/>: a middle point's legs merge at the first leg's speed, an end point's leg goes.</summary>
    public static Track Delete(Track track, int index)
    {
        ValidatePointIndex(track, index, "delete");
        var n = track.Points.Count;
        if (n == 1) return track with { Points = [], Timing = [] };

        var points = track.Points.ToList();
        points.RemoveAt(index);
        var timing = track.Timing.ToList();

        if (index == 0)
        {
            timing.RemoveAt(0);
            timing[0] = timing[0] with { LegSpeed = null };
            return track with { Points = points, Timing = timing };
        }

        if (index == n - 1)
        {
            timing.RemoveAt(index);
            return track with { Points = points, Timing = timing };
        }

        timing[index + 1] = timing[index + 1] with { LegSpeed = timing[index].LegSpeed };
        timing.RemoveAt(index);

        var before = LegLengths(track);
        var after = LegLengths(track with { Points = points });
        timing[index - 1] = ScaleOut(timing[index - 1], before[index] / after[index]);
        timing[index] = ScaleIn(timing[index], before[index + 1] / after[index]);
        return track with { Points = points, Timing = timing };
    }

    /// <summary>Moves point <paramref name="from"/> to position <paramref name="to"/>; holds travel with their point, leg speeds and easing stay in their slots.</summary>
    public static Track Move(Track track, int from, int to)
    {
        ValidatePointIndex(track, from, "move");
        ValidatePointIndex(track, to, "move");
        if (from == to) return track;

        var order = Enumerable.Range(0, track.Points.Count).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);

        var timing = order.Select((moved, slot) => track.Timing[slot] with { Hold = track.Timing[moved].Hold }).ToList();
        timing[0] = timing[0] with { LegSpeed = null };
        return track with { Points = order.Select(i => track.Points[i]).ToList(), Timing = timing };
    }

    /// <summary>Replaces point <paramref name="index"/>, keeping its timing.</summary>
    public static Track Replace(Track track, int index, ControlPoint point)
    {
        ValidatePointIndex(track, index, "replace");
        if (Equals(track.Points[index], point)) return track;

        var points = new List<ControlPoint>(track.Points) { [index] = point };
        return track with { Points = points };
    }

    /// <summary>Sets point <paramref name="index"/>'s hold, clamped to 0 to <see cref="MaxSeconds"/>; later keys shift with it.</summary>
    public static Track SetHold(Track track, int index, float seconds)
    {
        ValidatePointIndex(track, index, "hold");
        if (float.IsNaN(seconds)) return track;
        return WithTiming(track, index, track.Timing[index] with { Hold = Math.Clamp(seconds, 0f, MaxSeconds) });
    }

    /// <summary>Sets the playback mode; never touches points or timing.</summary>
    public static Track SetPlayback(Track track, PlaybackMode mode)
        => track.Playback == mode ? track : track with { Playback = mode };

    /// <summary>Sets the speed unpinned legs follow, clamped to <see cref="MinSpeed"/> to <see cref="MaxSpeed"/>.</summary>
    public static Track SetSpeed(Track track, float speed)
    {
        if (float.IsNaN(speed)) return track;
        var clamped = ClampSpeed(speed);
        return clamped == track.Speed ? track : track with { Speed = clamped };
    }

    /// <summary>Sets the track speed so the shot, holds included, takes <paramref name="seconds"/> as near as the ranges allow; unchanged when every leg is pinned.</summary>
    public static Track SetDuration(Track track, float seconds)
    {
        if (float.IsNaN(seconds) || AllPinned(track)) return track;

        var target = MathF.Min(seconds, MaxShotSeconds);
        var lengths = LegLengths(track);
        var fixedSeconds = track.Timing.Sum(t => (double)t.Hold);
        for (var leg = 1; leg < lengths.Length; leg++)
        {
            if (track.Timing[leg].LegSpeed is { } pinned) fixedSeconds += TimingCompiler.LegDuration(lengths[leg], pinned);
        }

        double Total(double speed)
        {
            var total = fixedSeconds;
            for (var leg = 1; leg < lengths.Length; leg++)
            {
                if (track.Timing[leg].LegSpeed is null) total += TimingCompiler.LegDuration(lengths[leg], (float)speed);
            }

            return total;
        }

        if (Total(MinSpeed) <= target) return SetSpeed(track, MinSpeed);
        if (Total(MaxSpeed) >= target) return SetSpeed(track, MaxSpeed);

        var low = Math.Log(MinSpeed);
        var high = Math.Log(MaxSpeed);
        for (var i = 0; i < DurationSteps; i++)
        {
            var mid = (low + high) / 2.0;
            if (Total(Math.Exp(mid)) > target) low = mid; else high = mid;
        }

        return SetSpeed(track, (float)Math.Exp((low + high) / 2.0));
    }

    /// <summary>Pins leg <paramref name="leg"/> at <paramref name="speed"/>, clamped to <see cref="MinSpeed"/> to <see cref="MaxSpeed"/>.</summary>
    public static Track SetLegSpeed(Track track, int leg, float speed)
    {
        ValidateLegIndex(track, leg);
        if (float.IsNaN(speed)) return track;
        return WithTiming(track, leg, track.Timing[leg] with { LegSpeed = ClampSpeed(speed) });
    }

    /// <summary>Pins leg <paramref name="leg"/> at the speed that takes <paramref name="seconds"/>, clamped to the leg range.</summary>
    public static Track SetLegDuration(Track track, int leg, float seconds)
    {
        ValidateLegIndex(track, leg);
        if (float.IsNaN(seconds)) return track;
        return SetLegSpeed(track, leg, LegLengths(track)[leg] / Math.Clamp(seconds, MinLegSeconds, MaxSeconds));
    }

    /// <summary>Unpins leg <paramref name="leg"/> so it follows the track speed again.</summary>
    public static Track ResetLeg(Track track, int leg)
    {
        ValidateLegIndex(track, leg);
        return WithTiming(track, leg, track.Timing[leg] with { LegSpeed = null });
    }

    /// <summary>The track with point <paramref name="index"/>'s timing replaced, or the same track when it is unchanged.</summary>
    internal static Track WithTiming(Track track, int index, PointTiming value)
    {
        if (track.Timing[index] == value) return track;
        var timing = track.Timing.ToList();
        timing[index] = value;
        return track with { Timing = timing };
    }

    internal static void ValidateLegIndex(Track track, int index)
    {
        var n = track.Points.Count;
        if (n < 2)
            throw new ArgumentOutOfRangeException(null, "this track has no legs");
        if (index < 1 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"leg index must be 1..{n - 1} for a {n}-point track");
    }

    internal static void ValidatePointIndex(Track track, int index, string what)
    {
        var n = track.Points.Count;
        if (n == 0)
            throw new ArgumentOutOfRangeException(null, "this track has no points");
        if (index < 0 || index > n - 1)
            throw new ArgumentOutOfRangeException(null, $"{what} index must be 0..{n - 1} for a {n}-point track");
    }

    private static float ClampSpeed(float speed) => Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>A Manual departure slope rescaled so it keeps its distance per second when its leg's length changes.</summary>
    private static PointTiming ScaleOut(PointTiming timing, float scale)
        => timing.OutMode == TangentMode.Manual ? timing with { OutTangent = timing.OutTangent * scale } : timing;

    /// <summary>A Manual arrival slope rescaled so it keeps its distance per second when its leg's length changes.</summary>
    private static PointTiming ScaleIn(PointTiming timing, float scale)
        => timing.InMode == TangentMode.Manual ? timing with { InTangent = timing.InTangent * scale } : timing;

    private static (int Point, KeyRole Role) Locate(Track track, int key)
    {
        var count = KeyCount(track);
        if (key < 0 || key >= count)
            throw new ArgumentOutOfRangeException(null, $"key index must be 0..{count - 1}");

        var first = 0;
        for (var p = 0; ; p++)
        {
            var next = first + (track.Timing[p].Hold > 0f ? 2 : 1);
            if (key < next) return (p, key == first ? KeyRole.Point : KeyRole.HoldEnd);
            first = next;
        }
    }
}
