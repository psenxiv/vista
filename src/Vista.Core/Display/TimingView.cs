using Vista.Core.Tracks;

namespace Vista.Core.Display;

/// <summary>The stretch of the shot, in seconds, that the timing graph shows.</summary>
public readonly record struct TimingView(float From, float To)
{
    /// <summary>The shortest stretch the graph zooms to.</summary>
    public const float MinSpan = 0.2f;

    public float Span => To - From;

    public static TimingView Whole(float duration) => new(0f, MathF.Max(duration, 0f));

    public bool IsWhole(float duration) => From <= 0f && To >= duration;

    /// <summary>Zooms by <paramref name="factor"/>, below 1 in and above 1 out, keeping <paramref name="anchor"/> where it is on screen and staying within the shot.</summary>
    public TimingView Zoom(float anchor, float factor, float duration)
    {
        if (!float.IsFinite(factor) || factor <= 0f || !float.IsFinite(anchor)) return this;
        var span = Math.Clamp(Span * factor, MathF.Min(MinSpan, MathF.Max(duration, 0f)), MathF.Max(duration, 0f));
        var fraction = Span > 0f ? Math.Clamp((anchor - From) / Span, 0f, 1f) : 0.5f;
        return Place(anchor - (fraction * span), span, duration);
    }

    /// <summary>Slides the view by <paramref name="seconds"/>, stopping at either end of the shot.</summary>
    public TimingView Pan(float seconds, float duration) => float.IsFinite(seconds) ? Place(From + seconds, Span, duration) : this;

    /// <summary>The view kept within a shot whose length has changed.</summary>
    public TimingView Clamp(float duration) => Place(From, Span, duration);

    /// <summary>The distances the curve covers across the view; a flat stretch widens to a yalm around it.</summary>
    public (float From, float To) Distances(TrackEvaluator evaluator)
    {
        var from = evaluator.DistanceAt(From);
        var to = evaluator.DistanceAt(To);
        if (to - from >= 1e-3f) return (from, to);
        var middle = (from + to) / 2f;
        return (middle - 0.5f, middle + 0.5f);
    }

    private static TimingView Place(float from, float span, float duration)
    {
        var length = MathF.Max(duration, 0f);
        var width = Math.Clamp(span, 0f, length);
        var start = Math.Clamp(from, 0f, length - width);
        return new(start, start + width);
    }
}
