using System.Numerics;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Display;

/// <summary>The timing graph's plot: time across, distance up, and the maths for its handles.</summary>
public readonly record struct TimingGraph(Vector2 Origin, Vector2 Size, float Duration, float Distance)
{
    /// <summary>Seconds or yalms past the view's edge that still count as in it, so a tick or key on the edge isn't lost to rounding.</summary>
    private const float Slack = 1e-4f;

    /// <summary>The first second in view; 0 unless zoomed.</summary>
    public float TimeFrom { get; init; }

    /// <summary>The last second in view; the whole shot unless zoomed.</summary>
    public float TimeTo { get; init; } = Duration;

    /// <summary>The distance at the plot's bottom edge; 0 unless zoomed.</summary>
    public float DistanceFrom { get; init; }

    /// <summary>The distance at the plot's top edge; the whole path unless zoomed.</summary>
    public float DistanceTo { get; init; } = Distance;

    private float TimeRange => MathF.Max(TimeTo - TimeFrom, 1e-3f);
    private float DistanceRange => MathF.Max(DistanceTo - DistanceFrom, 1e-3f);

    /// <summary>The times of ticks <paramref name="step"/> seconds apart across the view.</summary>
    public IReadOnlyList<float> TickTimes(float step)
    {
        var ticks = new List<float>();
        for (var k = (int)MathF.Ceiling((TimeFrom / step) - Slack); k * step <= TimeTo + Slack; k++)
            ticks.Add(k * step);
        return ticks;
    }

    /// <summary>The distances of ticks <paramref name="step"/> yalms apart up the view, leaving out zero.</summary>
    public IReadOnlyList<float> TickDistances(float step)
    {
        var ticks = new List<float>();
        for (
            var k = Math.Max(1, (int)MathF.Ceiling((DistanceFrom / step) - Slack));
            k * step <= DistanceTo + Slack;
            k++
        )
            ticks.Add(k * step);
        return ticks;
    }

    /// <summary>Whether <paramref name="time"/> is in view.</summary>
    public bool ShowsTime(float time) => time >= TimeFrom - Slack && time <= TimeTo + Slack;

    /// <summary>Whether <paramref name="distance"/> is in view; not a number counts as in view.</summary>
    public bool ShowsDistance(float distance) => !(distance < DistanceFrom - Slack || distance > DistanceTo + Slack);

    /// <summary>Whether <paramref name="point"/> falls inside the plot rectangle.</summary>
    public bool Contains(Vector2 point) =>
        point.X >= Origin.X && point.X <= Origin.X + Size.X && point.Y >= Origin.Y && point.Y <= Origin.Y + Size.Y;

    /// <summary>The pixel for a time and a distance.</summary>
    public Vector2 ToScreen(float time, float distance) =>
        new(
            Origin.X + ((time - TimeFrom) / TimeRange * Size.X),
            Origin.Y + Size.Y - ((distance - DistanceFrom) / DistanceRange * Size.Y)
        );

    /// <summary>The time under pixel column <paramref name="x"/>, clamped to the view.</summary>
    public float TimeAt(float x) => TimeFrom + (Math.Clamp((x - Origin.X) / Size.X, 0f, 1f) * (TimeTo - TimeFrom));

    /// <summary>The time under pixel column <paramref name="x"/>, clamped at the view's start but not its end, so a key dragged past the plot can lengthen the shot.</summary>
    public float TimeAtOpenEnded(float x) => TimeFrom + (MathF.Max((x - Origin.X) / Size.X, 0f) * (TimeTo - TimeFrom));

    /// <summary>The distance under pixel row <paramref name="y"/>, clamped to the view.</summary>
    public float DistanceAt(float y) =>
        DistanceFrom + (Math.Clamp((Origin.Y + Size.Y - y) / Size.Y, 0f, 1f) * (DistanceTo - DistanceFrom));

    /// <summary>The end of a handle <paramref name="length"/> pixels long leaving <paramref name="key"/> at <paramref name="slope"/> distance per second.</summary>
    public Vector2 HandleEnd(Vector2 key, KeySide side, float slope, float length)
    {
        var direction = Vector2.Normalize(new Vector2(Size.X / TimeRange, -slope * Size.Y / DistanceRange));
        return key + ((side == KeySide.In ? -direction : direction) * length);
    }

    /// <summary>The slope, in distance per second, of a handle dragged to <paramref name="cursor"/>; never negative.</summary>
    public float SlopeFromHandle(Vector2 key, KeySide side, Vector2 cursor)
    {
        var offset = side == KeySide.In ? key - cursor : cursor - key;
        var across = MathF.Max(offset.X, 1f) / Size.X * TimeRange;
        var up = MathF.Max(-offset.Y, 0f) / Size.Y * DistanceRange;
        return up / across;
    }
}
