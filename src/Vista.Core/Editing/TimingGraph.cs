using System.Numerics;
using Vista.Core.Tracks;

namespace Vista.Core.Editing;

/// <summary>The timing graph's plot: time across, distance up, and the maths for its handles.</summary>
public readonly record struct TimingGraph(Vector2 Origin, Vector2 Size, float Duration, float Distance)
{
    private float SafeDuration => MathF.Max(Duration, 1e-3f);
    private float SafeDistance => MathF.Max(Distance, 1e-3f);

    /// <summary>The pixel for a time and a distance.</summary>
    public Vector2 ToScreen(float time, float distance)
        => new(Origin.X + (time / SafeDuration * Size.X), Origin.Y + Size.Y - (distance / SafeDistance * Size.Y));

    /// <summary>The time under pixel column <paramref name="x"/>, clamped to the shot.</summary>
    public float TimeAt(float x) => Math.Clamp((x - Origin.X) / Size.X, 0f, 1f) * Duration;

    /// <summary>The distance under pixel row <paramref name="y"/>, clamped to the path.</summary>
    public float DistanceAt(float y) => Math.Clamp((Origin.Y + Size.Y - y) / Size.Y, 0f, 1f) * Distance;

    /// <summary>The end of a handle <paramref name="length"/> pixels long leaving <paramref name="key"/> at <paramref name="slope"/> distance per second.</summary>
    public Vector2 HandleEnd(Vector2 key, KeySide side, float slope, float length)
    {
        var direction = Vector2.Normalize(new Vector2(Size.X / SafeDuration, -slope * Size.Y / SafeDistance));
        return key + ((side == KeySide.In ? -direction : direction) * length);
    }

    /// <summary>The slope, in distance per second, of a handle dragged to <paramref name="cursor"/>; never negative.</summary>
    public float SlopeFromHandle(Vector2 key, KeySide side, Vector2 cursor)
    {
        var offset = side == KeySide.In ? key - cursor : cursor - key;
        var across = MathF.Max(offset.X, 1f) / Size.X * SafeDuration;
        var up = MathF.Max(-offset.Y, 0f) / Size.Y * SafeDistance;
        return up / across;
    }
}
