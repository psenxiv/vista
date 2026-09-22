namespace Vista.Core.Tracks;

/// <summary>Maps time in the shot to a place on the path, in path units, via a monotone cubic Hermite curve.</summary>
public sealed class TimingCurve
{
    /// <summary>Below this, a secant is treated as a hold rather than divided by.</summary>
    private const float SecantEpsilon = 1e-6f;

    /// <summary>Fritsch-Carlson monotonicity bound on a single tangent-to-secant ratio.</summary>
    private const float MonotoneBound = 3f;

    private readonly IReadOnlyList<TimingKey> _keys;
    private readonly float[] _times;
    private readonly float[] _inTangent = Array.Empty<float>();
    private readonly float[] _outTangent = Array.Empty<float>();

    /// <summary>Total shot length: the last key's time, 0 with no keys.</summary>
    public double Duration { get; }

    /// <summary>Builds the curve through <paramref name="keys"/>.</summary>
    public TimingCurve(IReadOnlyList<TimingKey> keys)
    {
        Validate(keys);
        _keys = keys;
        _times = keys.Select(k => k.Time).ToArray();
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
        return Hermite.At(k0.Position, k1.Position, m0, m1, localT);
    }

    private int FindInterval(double t) => Search.LastAtOrBelow(_times, t, 0, _keys.Count - 1);

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
            var auto = k == 0 ? delta[0]
                : k == n - 1 ? delta[n - 2]
                : InteriorRaw(delta[k - 1], delta[k], h[k - 1], h[k]);
            rawIn[k] = SideRaw(key.InMode, key.InTangent, auto, k > 0 ? delta[k - 1] : 0f);
            rawOut[k] = SideRaw(key.OutMode, key.OutTangent, auto, k < n - 1 ? delta[k] : 0f);
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

    /// <summary>One side's slope before the monotone clamp; a Manual tangent is a ratio to <paramref name="secant"/>.</summary>
    private static float SideRaw(TangentMode mode, float manual, float auto, float secant) => mode switch
    {
        TangentMode.Auto => auto,
        TangentMode.Linear => secant,
        TangentMode.Flat => 0f,
        TangentMode.Manual => manual * secant,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"unknown tangent mode {mode}"),
    };

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
            if (!Enum.IsDefined(keys[i].InMode) || !Enum.IsDefined(keys[i].OutMode))
                throw new ArgumentException($"timing key {i} has an unknown tangent mode");
            if (!float.IsFinite(keys[i].InTangent) || !float.IsFinite(keys[i].OutTangent))
                throw new ArgumentException($"timing key {i} has a non-finite tangent");
            if (i == 0) continue;
            if (keys[i].Time <= keys[i - 1].Time)
                throw new ArgumentException("timing keys must have strictly increasing times");
            if (keys[i].Position < keys[i - 1].Position)
                throw new ArgumentException("timing key positions must not decrease");
        }
    }

    /// <summary>The resolved slope on one side of key <paramref name="index"/>, in position per second; 0 on a side with no span.</summary>
    public float SideSlope(int index, KeySide side)
    {
        if (_keys.Count < 2) return 0f;
        return side == KeySide.In ? _inTangent[index] : _outTangent[index];
    }

    /// <summary>The curve's slope at <paramref name="time"/>, in position per second; 0 outside the keys.</summary>
    public float SlopeAt(double time)
    {
        if (_keys.Count < 2 || time <= _keys[0].Time || time >= Duration) return 0f;

        var k = FindInterval(time);
        var span = _keys[k + 1].Time - _keys[k].Time;
        var t = (float)((time - _keys[k].Time) / span);
        var t2 = t * t;
        var m0 = _outTangent[k] * span;
        var m1 = _inTangent[k + 1] * span;
        var d = (((6f * t2) - (6f * t)) * _keys[k].Position)
              + (((3f * t2) - (4f * t) + 1f) * m0)
              + (((-6f * t2) + (6f * t)) * _keys[k + 1].Position)
              + (((3f * t2) - (2f * t)) * m1);
        return d / span;
    }
}
