namespace Vista.Core.Tracks.Timing;

/// <summary>A leg's easing preset, or Custom when its sides match none.</summary>
public enum Easing { Smooth, Linear, EaseIn, EaseOut, EaseInOut, Custom }

/// <summary>Maps easing presets onto the two sides that bound a leg.</summary>
public static class LegEasing
{
    /// <summary>The presets, in menu order; Custom is not one of them.</summary>
    public static readonly IReadOnlyList<Easing> Presets = [Easing.Smooth, Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];

    /// <summary>The out mode of a leg's first key and the in mode of its last key for <paramref name="easing"/>.</summary>
    public static (TangentMode Out, TangentMode In) Modes(Easing easing) => easing switch
    {
        Easing.Smooth => (TangentMode.Auto, TangentMode.Auto),
        Easing.Linear => (TangentMode.Linear, TangentMode.Linear),
        Easing.EaseIn => (TangentMode.Flat, TangentMode.Auto),
        Easing.EaseOut => (TangentMode.Auto, TangentMode.Flat),
        Easing.EaseInOut => (TangentMode.Flat, TangentMode.Flat),
        _ => throw new ArgumentOutOfRangeException(nameof(easing), "custom easing has no modes"),
    };

    /// <summary>The preset leg <paramref name="leg"/>'s bounding sides match, or Custom.</summary>
    public static Easing Read(Track track, int leg)
    {
        TrackEditing.ValidateLegIndex(track, leg);
        var sides = (track.Timing[leg - 1].OutMode, track.Timing[leg].InMode);
        foreach (var preset in Presets)
        {
            if (Modes(preset) == sides) return preset;
        }

        return Easing.Custom;
    }

    /// <summary>Sets leg <paramref name="leg"/>'s bounding sides to <paramref name="easing"/>, leaving its time alone.</summary>
    public static Track Set(Track track, int leg, Easing easing)
    {
        var (outMode, inMode) = Modes(easing);
        if (Read(track, leg) == easing) return track;

        var timing = track.Timing.ToList();
        timing[leg - 1] = timing[leg - 1] with { OutMode = outMode, OutTangent = 0f };
        timing[leg] = timing[leg] with { InMode = inMode, InTangent = 0f };
        return track with { Timing = timing };
    }
}
