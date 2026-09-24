using System.Numerics;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Display;

/// <summary>The timing graph's plot: time across, distance up, and the maths for its handles.</summary>
public readonly record struct TimingGraph(Vector2 Origin, Vector2 Size, float Duration, float Distance)
{
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

    /// <summary>The pixel for a time and a distance.</summary>
    public Vector2 ToScreen(float time, float distance)
        => new(Origin.X + ((time - TimeFrom) / TimeRange * Size.X), Origin.Y + Size.Y - ((distance - DistanceFrom) / DistanceRange * Size.Y));

    /// <summary>The time under pixel column <paramref name="x"/>, clamped to the view.</summary>
    public float TimeAt(float x) => TimeFrom + (Math.Clamp((x - Origin.X) / Size.X, 0f, 1f) * (TimeTo - TimeFrom));

    /// <summary>The time under pixel column <paramref name="x"/>, clamped at the view's start but not its end, so a key dragged past the plot can lengthen the shot.</summary>
    public float TimeAtOpenEnded(float x) => TimeFrom + (MathF.Max((x - Origin.X) / Size.X, 0f) * (TimeTo - TimeFrom));

    /// <summary>The distance under pixel row <paramref name="y"/>, clamped to the view.</summary>
    public float DistanceAt(float y) => DistanceFrom + (Math.Clamp((Origin.Y + Size.Y - y) / Size.Y, 0f, 1f) * (DistanceTo - DistanceFrom));

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
