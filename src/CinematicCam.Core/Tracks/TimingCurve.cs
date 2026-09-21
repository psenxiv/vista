namespace CinematicCam.Core.Tracks;

/// <summary>Maps elapsed time to a place on the path, in path units, via a monotone cubic Hermite curve.</summary>
public sealed class TimingCurve
{
    /// <summary>Below this, a secant is treated as a hold rather than divided by.</summary>
    private const float SecantEpsilon = 1e-6f;

    /// <summary>Fritsch-Carlson monotonicity bound on a single tangent-to-secant ratio.</summary>
    private const float MonotoneBound = 3f;

    private readonly IReadOnlyList<TimingKey> _keys;
    private readonly float[] _inTangent = Array.Empty<float>();
    private readonly float[] _outTangent = Array.Empty<float>();

    /// <summary>Total shot length: the last key's time, 0 with no keys.</summary>
    public double Duration { get; }

    /// <summary>Builds the curve through <paramref name="keys"/>.</summary>
    public TimingCurve(IReadOnlyList<TimingKey> keys)
    {
        Validate(keys);
        _keys = keys;
        Duration = keys.Count == 0 ? 0.0 : keys[^1].Time;

        if (keys.Count >= 2)
            (_inTangent, _outTangent) = BuildTangents(keys);
    }

    /// <summary>Place on the path at <paramref name="time"/>, in path units. Holds end values.</summary>
    public float PositionAt(double time)
    {
        var n = _keys.Count;
        if (n == 0) return 0f;
        if (n == 1) return _keys[0].Position;

        if (time <= _keys[0].Time) return _keys[0].Position;
        if (time >= Duration) return _keys[^1].Position;

        var k = FindInterval(time);
        var k0 = _keys[k];
        var k1 = _keys[k + 1];
        var span = k1.Time - k0.Time;
        var localT = span <= 0f ? 0f : (float)((time - k0.Time) / span);

        var m0 = _outTangent[k] * span;
        var m1 = _inTangent[k + 1] * span;
        return Hermite(k0.Position, k1.Position, m0, m1, localT);
    }

    private int FindInterval(double t)
    {
        var lo = 0;
        var hi = _keys.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_keys[mid].Time <= t) lo = mid; else hi = mid;
        }

        return lo;
    }

    private static float Hermite(float p0, float p1, float m0, float m1, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = (2f * t3) - (3f * t2) + 1f;
        var h10 = t3 - (2f * t2) + t;
        var h01 = (-2f * t3) + (3f * t2);
        var h11 = t3 - t2;
        return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
    }

    /// <summary>Raw (pre-clamp) in/out tangent per key, then a monotonicity clamp per interval.</summary>
    private static (float[] InTangent, float[] OutTangent) BuildTangents(IReadOnlyList<TimingKey> keys)
    {
        var n = keys.Count;
        var delta = new float[n - 1];
        var h = new float[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            h[i] = keys[i + 1].Time - keys[i].Time;
            delta[i] = (keys[i + 1].Position - keys[i].Position) / h[i];
        }

        var rawIn = new float[n];
        var rawOut = new float[n];
        for (var k = 0; k < n; k++)
        {
            var key = keys[k];
            switch (key.Mode)
            {
                case TangentMode.Flat:
                    rawIn[k] = 0f;
                    rawOut[k] = 0f;
                    break;

                case TangentMode.Manual:
                    rawIn[k] = key.InTangent;
                    rawOut[k] = key.OutTangent;
                    break;

                case TangentMode.Linear:
                    rawIn[k] = k > 0 ? delta[k - 1] : 0f;
                    rawOut[k] = k < n - 1 ? delta[k] : 0f;
                    break;

                case TangentMode.Auto:
                    float value;
                    if (k == 0)
                        value = delta[0];
                    else if (k == n - 1)
                        value = delta[n - 2];
                    else
                        value = InteriorRaw(delta[k - 1], delta[k], h[k - 1], h[k]);
                    rawIn[k] = value;
                    rawOut[k] = value;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(keys), $"unknown tangent mode {key.Mode}");
            }
        }

        var inTangent = new float[n];
        var outTangent = new float[n];
        for (var i = 0; i < n - 1; i++)
        {
            var (m0, m1) = ClampPair(rawOut[i], rawIn[i + 1], delta[i]);
            outTangent[i] = m0;
            inTangent[i + 1] = m1;
        }

        return (inTangent, outTangent);
    }

    /// <summary>PCHIP weighted harmonic mean of the two neighbouring secants; 0 if either is a hold.</summary>
    private static float InteriorRaw(float deltaPrev, float deltaNext, float hPrev, float hNext)
    {
        if (deltaPrev <= SecantEpsilon || deltaNext <= SecantEpsilon) return 0f;

        var w1 = (2f * hNext) + hPrev;
        var w2 = hNext + (2f * hPrev);
        return (w1 + w2) / ((w1 / deltaPrev) + (w2 / deltaNext));
    }

    /// <summary>Clamps an interval's tangent pair to the [0,3] square per ratio so its cubic stays monotone; a zero secant zeroes both.</summary>
    private static (float M0, float M1) ClampPair(float m0, float m1, float delta)
    {
        if (delta <= SecantEpsilon) return (0f, 0f);

        var a = Math.Clamp(m0 / delta, 0f, MonotoneBound);
        var b = Math.Clamp(m1 / delta, 0f, MonotoneBound);
        return (a * delta, b * delta);
    }

    private static void Validate(IReadOnlyList<TimingKey> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (!Enum.IsDefined(keys[i].Mode))
                throw new ArgumentException($"timing key {i} has an unknown tangent mode");
            if (i == 0) continue;
            if (keys[i].Time <= keys[i - 1].Time)
                throw new ArgumentException("timing keys must have strictly increasing times");
            if (keys[i].Position < keys[i - 1].Position)
                throw new ArgumentException("timing key positions must not decrease");
        }
    }
}
