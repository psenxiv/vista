using Vista.Core.Scenes;

namespace Vista.Core.Session;

/// <summary>Decides when a changed scene has been still long enough to save.</summary>
public sealed class SaveDebounce
{
    /// <summary>How long a changed scene must stay unchanged before it is due, in seconds.</summary>
    public const double DelaySeconds = 1.0;

    private Scene saved;
    private Scene seen;
    private double seenSince;

    /// <summary>Starts with <paramref name="saved"/> as the scene on disk.</summary>
    public SaveDebounce(Scene saved)
    {
        this.saved = saved;
        seen = saved;
    }

    /// <summary>Feeds the scene at time <paramref name="now"/>, in seconds; true once it has differed from the saved scene, unchanged by reference, for DelaySeconds.</summary>
    public bool Due(Scene current, double now)
    {
        if (!ReferenceEquals(current, seen))
        {
            seen = current;
            seenSince = now;
        }

        return !ReferenceEquals(current, saved) && now - seenSince >= DelaySeconds;
    }

    /// <summary>Records <paramref name="scene"/> as saved.</summary>
    public void Saved(Scene scene) => saved = scene;
}
