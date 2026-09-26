using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Timing;

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

    /// <summary>The world angular velocity (a rotation vector: axis times radians per second) of <paramref name="rotation"/> at <paramref name="t"/>, by central difference over 2·<paramref name="h"/> seconds: q(t+h)·q(t−h)⁻¹, sign-fixed to W ≥ 0, as axis × angle / (2h).</summary>
    internal static Vector3 WorldTurnRate(Func<double, Quaternion> rotation, double t, double h)
    {
        var relative = Quaternion.Concatenate(Quaternion.Inverse(rotation(t - h)), rotation(t + h));
        if (relative.W < 0f)
            relative = Quaternion.Negate(relative);
        var axis = new Vector3(relative.X, relative.Y, relative.Z);
        var sine = axis.Length();
        var angle = 2f * MathF.Atan2(sine, relative.W);
        var vector = sine < 1e-9f ? axis * 2f : axis / sine * angle;
        return vector / (2f * (float)h);
    }

    /// <summary>A recorded-aim track's rotation over time, read the way <see cref="TrackEvaluator.Evaluate"/> builds its camera state, for measuring the world turn rate.</summary>
    internal static Func<double, Quaternion> RecordedAimRotation(TrackEvaluator evaluator) =>
        t =>
        {
            var frame = evaluator.Evaluate(t)!.Value;
            return CameraRotation.FromBasis(frame.Forward, frame.Up);
        };
}
