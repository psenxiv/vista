namespace Vista.Core.Display;

/// <summary>A timing graph's or scrub bar's view kept between frames: whole until zoomed, and whole again when what it shows changes.</summary>
public sealed class ViewZoom<TKey>
    where TKey : IEquatable<TKey>
{
    private TimingView? zoomed;
    private TKey? key;
    private bool keyed;

    /// <summary>True when the view shows less than the whole length.</summary>
    public bool Zoomed => zoomed is not null;

    /// <summary>True when the last <see cref="View"/> met a new key and went back to the whole length.</summary>
    public bool Restarted { get; private set; }

    /// <summary>This frame's view of something <paramref name="length"/> seconds long, identified by <paramref name="key"/>.</summary>
    public TimingView View(TKey key, float length)
    {
        Restarted = !keyed || !key.Equals(this.key);
        if (Restarted)
        {
            zoomed = null;
            this.key = key;
            keyed = true;
        }

        zoomed = zoomed?.Clamp(length).UnlessWhole(length);
        return zoomed ?? TimingView.Whole(length);
    }

    /// <summary>Zooms by wheel <paramref name="notches"/>, positive in, around <paramref name="anchor"/> seconds.</summary>
    public void Zoom(float anchor, float notches, float length)
    {
        if (notches == 0f)
            return;
        var from = zoomed ?? TimingView.Whole(length);
        zoomed = from.Zoom(anchor, MathF.Pow(TimingView.ZoomPerNotch, -notches), length).UnlessWhole(length);
    }

    /// <summary>Shows <paramref name="view"/>, as a pan leaves it.</summary>
    public void Set(TimingView view, float length) => zoomed = view.UnlessWhole(length);

    /// <summary>Shows the whole length.</summary>
    public void Reset() => zoomed = null;
}
