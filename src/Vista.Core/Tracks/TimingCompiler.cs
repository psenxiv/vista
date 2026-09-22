namespace Vista.Core.Tracks;

/// <summary>Builds a track's timing keys from its leg speeds, holds and easing.</summary>
public static class TimingCompiler
{
    /// <summary>A leg's time at a speed, clamped to the leg range.</summary>
    public static float LegDuration(float length, float speed)
        => Math.Clamp(length / speed, TrackEditing.MinLegSeconds, TrackEditing.MaxSeconds);

    /// <summary>One key per point and one per hold end; <paramref name="legLengths"/> is indexed by leg.</summary>
    public static IReadOnlyList<TimingKey> Compile(Track track, IReadOnlyList<float> legLengths)
    {
        if (track.Timing.Count != track.Points.Count) throw new ArgumentException("every point needs one timing entry");

        var keys = new List<TimingKey>(track.Points.Count * 2);
        var time = 0f;
        for (var p = 0; p < track.Points.Count; p++)
        {
            var t = track.Timing[p];
            if (p > 0) time += LegDuration(legLengths[p], t.LegSpeed ?? track.Speed);

            var holds = t.Hold > 0f;
            keys.Add(new TimingKey(time, p, t.InMode, holds ? TangentMode.Auto : t.OutMode, t.InTangent, holds ? 0f : t.OutTangent, t.Broken));
            if (!holds) continue;

            time += t.Hold;
            keys.Add(new TimingKey(time, p, TangentMode.Auto, t.OutMode, 0f, t.OutTangent, t.Broken));
        }

        return keys;
    }
}
