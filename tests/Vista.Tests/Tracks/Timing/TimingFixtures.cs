using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;
using Xunit;

namespace Vista.Tests.Tracks.Timing;

/// <summary>Key times and key drags the timing tests share.</summary>
internal static class TimingFixtures
{
    /// <summary>The track's key times, rounded to hundredths of a second.</summary>
    internal static float[] Times(Track track) =>
        new TrackEvaluator(track).Keys.Select(k => MathF.Round(k.Time, 2)).ToArray();

    /// <summary>Drags key <paramref name="key"/> towards <paramref name="time"/>, as the timing graph does.</summary>
    internal static Track MoveKey(Track track, int key, float time) =>
        TimingEditing.MoveKey(track, new TrackEvaluator(track), key, time);

    /// <summary>Asserts <paramref name="track"/>'s key times match <paramref name="expected"/> within 0.05 s, naming the mismatched key.</summary>
    internal static void AssertTimes(float[] expected, Track track)
    {
        var actual = Times(track);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(
                MathF.Abs(expected[i] - actual[i]) <= 0.05f,
                $"key {i}: expected {expected[i]}, got {actual[i]} in [{string.Join(", ", actual)}]"
            );
    }
}
