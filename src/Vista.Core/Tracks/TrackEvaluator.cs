using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks;

/// <summary>Turns a track and a moment in time into where the camera is, looks and its field of view.</summary>
public sealed class TrackEvaluator
{
    /// <summary>Metres a segment counts as when timing, so a leg between coincident points still takes its time.</summary>
    public const float MinTimingLength = 0.1f;

    private readonly Track _track;
    private readonly Vector3[] _positions;
    private readonly ArcLengthTable _table;
    private readonly float[] _lengths;
    private readonly float[] _distances;
    private readonly IReadOnlyList<TimingKey> _keys;
    private readonly TimingKey[] _distanceKeys;
    private readonly TimingCurve _curve;
    private readonly float[] _yaws;
    private readonly float[] _pitches;
    private readonly TimedChannel? _yaw;
    private readonly TimedChannel? _pitch;
    private readonly TimedChannel? _roll;
    private readonly TimedChannel? _fov;
    private readonly float _fovMin;
    private readonly float _fovMax;

    /// <summary>Total shot length: the compiled last key's time, 0 with no points.</summary>
    public double Duration => _curve.Duration;

    /// <summary>The track's compiled timing keys, positions in control-point units.</summary>
    public IReadOnlyList<TimingKey> Keys => _keys;

    /// <summary>The path's length as timing measures it, each segment at least <see cref="MinTimingLength"/>.</summary>
    public float TotalDistance => _distances[^1];

    /// <summary>Builds the spline, arc-length table, compiled keys, distance timing curve and timed aim channels once for <paramref name="track"/>.</summary>
    public TrackEvaluator(Track track)
    {
        _track = track;
        _positions = track.Points.Select(p => p.Position).ToArray();
        _table = new ArcLengthTable(_positions);
        _lengths = _table.SegmentLengths(MinTimingLength);
        _distances = new float[_lengths.Length + 1];
        for (var i = 0; i < _lengths.Length; i++) _distances[i + 1] = _distances[i] + _lengths[i];

        var legLengths = new float[track.Points.Count];
        for (var leg = 1; leg < legLengths.Length; leg++) legLengths[leg] = _lengths[leg - 1];
        _keys = TimingCompiler.Compile(track, legLengths);
        _distanceKeys = _keys.Select(ToDistance).ToArray();
        _curve = new TimingCurve(_distanceKeys);
        _yaws = TrackAim.UnwrapAngles(track.Points.Select(p => p.Yaw).ToArray());
        _pitches = track.Points.Select(p => p.Pitch).ToArray();
        var fovs = track.Points.Select(p => p.Fov).ToArray();
        _fovMin = fovs.Length == 0 ? 0f : fovs.Min();
        _fovMax = fovs.Length == 0 ? 0f : fovs.Max();
        if (track.Points.Count == 0) return;

        // Each point is reached at its key's time and left at its hold end's, or at once.
        var arrive = Enumerable.Range(0, track.Points.Count).Select(PointSeconds).ToArray();
        var depart = arrive.Select((at, i) => track.Timing[i].Hold > 0f ? _keys[TrackEditing.PointKey(track, i) + 1].Time : at).ToArray();
        _yaw = new TimedChannel(_yaws, arrive, depart);
        _pitch = new TimedChannel(_pitches, arrive, depart);
        _roll = new TimedChannel(TrackAim.UnwrapAngles(track.Points.Select(p => p.Roll).ToArray()), arrive, depart);
        _fov = new TimedChannel(fovs, arrive, depart);
    }

    /// <summary>Leg <paramref name="leg"/>'s length as timing measures it.</summary>
    public float LegLength(int leg)
    {
        TrackEditing.ValidateLegIndex(_track, leg);
        return _lengths[leg - 1];
    }

    /// <summary>The time leg <paramref name="leg"/> takes, from its start key to its end key.</summary>
    public float LegSeconds(int leg) => _keys[TrackEditing.LegEndKey(_track, leg)].Time - _keys[TrackEditing.LegStartKey(_track, leg)].Time;

    /// <summary>The time point <paramref name="point"/> is reached.</summary>
    public float PointSeconds(int point) => _keys[TrackEditing.PointKey(_track, point)].Time;

    /// <summary>The leg whose time span holds <paramref name="time"/>, or null in a hold or outside the shot.</summary>
    public int? LegAt(float time)
    {
        for (var leg = 1; leg < _track.Points.Count; leg++)
        {
            if (time >= _keys[TrackEditing.LegStartKey(_track, leg)].Time && time <= _keys[TrackEditing.LegEndKey(_track, leg)].Time) return leg;
        }

        return null;
    }

    /// <summary>The camera's state at <paramref name="time"/>, aimed at <paramref name="target"/> when given, or null for a track with no points.</summary>
    public CameraState? Evaluate(double time, Vector3? target = null)
    {
        if (_track.Points.Count == 0) return null;

        if (_track.Points.Count == 1)
        {
            var only = _track.Points[0];
            var (onlyYaw, onlyPitch) = Toward(only.Position, target) ?? (only.Yaw, only.Pitch);
            return new CameraState(only.Position, FreeCamMotion.LookAtFrom(only.Position, onlyYaw, onlyPitch), only.Fov, only.Roll);
        }

        var (cameraPosition, segment, fraction) = PlaceAt(time);

        var (yaw, pitch) = Toward(cameraPosition, target)
            ?? (_track.Aim == AimMode.PathTangent
                ? LookAhead(time, cameraPosition) ?? TrackAim.PathTangent(_positions, _table, segment, fraction, (_yaws[0], _pitches[0]))
                : AimKeys(time));

        var fov = Math.Clamp(_fov!.At(time), _fovMin, _fovMax);
        var roll = _roll!.At(time);

        return new CameraState(cameraPosition, FreeCamMotion.LookAtFrom(cameraPosition, yaw, pitch), fov, roll);
    }

    /// <summary>The aim at <paramref name="target"/> from <paramref name="from"/>, or null with no target or one on the camera.</summary>
    private static (float Yaw, float Pitch)? Toward(Vector3 from, Vector3? target)
        => target is { } at ? TrackAim.Toward(from, at) : null;

    /// <summary>Distance along the path at <paramref name="time"/>.</summary>
    public float DistanceAt(double time) => _curve.PositionAt(time);

    /// <summary>Speed along the path at <paramref name="time"/>, in distance per second.</summary>
    public float SlopeAt(double time) => _curve.SlopeAt(time);

    /// <summary>The resolved slope on one side of timing key <paramref name="key"/>, in distance per second.</summary>
    public float SideSlope(int key, KeySide side) => _curve.SideSlope(key, side);

    /// <summary>Distance along the path of a place in control-point units.</summary>
    public float DistanceOf(float position)
    {
        if (_lengths.Length == 0) return 0f;
        var clamped = Math.Clamp(position, 0f, _lengths.Length);
        var segment = Math.Min((int)MathF.Floor(clamped), _lengths.Length - 1);
        return _distances[segment] + ((clamped - segment) * _lengths[segment]);
    }

    /// <summary>The place in control-point units at <paramref name="distance"/> along the path.</summary>
    public float PositionOf(float distance)
    {
        if (_lengths.Length == 0) return 0f;
        var (segment, fraction) = LocateDistance(distance);
        return segment + fraction;
    }

    /// <summary>A slope on one side of key <paramref name="key"/>, from distance per second to a ratio of that span's average speed.</summary>
    public float ToStoredSlope(int key, KeySide side, float distancePerSecond)
    {
        var secant = Secant(key, side);
        return secant == 0f ? 0f : distancePerSecond / secant;
    }

    /// <summary>A slope on one side of key <paramref name="key"/>, from a ratio of that span's average speed to distance per second.</summary>
    public float FromStoredSlope(int key, KeySide side, float stored) => stored * Secant(key, side);

    /// <summary>The average speed, in distance per second, of the span on one side of key <paramref name="key"/>; 0 with no span.</summary>
    private float Secant(int key, KeySide side)
    {
        var start = side == KeySide.In ? key - 1 : key;
        if (start < 0 || start + 1 >= _distanceKeys.Length) return 0f;
        var a = _distanceKeys[start];
        var b = _distanceKeys[start + 1];
        return (b.Position - a.Position) / (b.Time - a.Time);
    }

    /// <summary>The key with its position moved from control-point units to distance along the path.</summary>
    private TimingKey ToDistance(TimingKey key)
        => _lengths.Length == 0 ? key : key with { Position = DistanceOf(key.Position) };

    /// <summary>Splits a distance along the path into a segment index and the arc fraction into it.</summary>
    private (int Segment, float Fraction) LocateDistance(float distance)
    {
        var last = _lengths.Length - 1;
        if (distance >= _distances[^1]) return (last, 1f);
        if (distance <= 0f) return (0, 0f);

        var lo = Search.LastAtOrBelow(_distances, distance, 0, _lengths.Length);

        return (lo, Math.Clamp((distance - _distances[lo]) / _lengths[lo], 0f, 1f));
    }

    private (float Yaw, float Pitch) AimKeys(double time)
        => (_yaw!.At(time), Math.Clamp(_pitch!.At(time), -TrackAim.PitchLimit, TrackAim.PitchLimit));

    /// <summary>The aim from <paramref name="from"/> to where the path is the track's look-ahead later, the end once past it, or null when that's too close to give a steady direction.</summary>
    private (float Yaw, float Pitch)? LookAhead(double time, Vector3 from)
        => _track.LookAhead > 0f ? TrackAim.Toward(from, PlaceAt(time + _track.LookAhead).Position) : null;

    /// <summary>Where the camera is on the path at <paramref name="time"/>, and the segment and arc fraction it's in.</summary>
    private (Vector3 Position, int Segment, float Fraction) PlaceAt(double time)
    {
        var (segment, fraction) = LocateDistance(_curve.PositionAt(time));
        return (CatmullRom.Evaluate(_positions, segment, _table.ParameterAt(segment, fraction)), segment, fraction);
    }
}
