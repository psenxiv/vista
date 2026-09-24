using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Spline;
using Vista.Core.Tracks.Timing;

namespace Vista.Core.Tracks;

/// <summary>Turns a track and a moment in time into where the camera is, looks and its field of view.</summary>
public sealed class TrackEvaluator
{
    /// <summary>Metres a segment counts as when timing, so a leg between coincident points still takes its time.</summary>
    public const float MinTimingLength = 0.1f;

    /// <summary>Yalms within which the look-ahead aim blends from the spot ahead towards the path's direction into it.</summary>
    public const float LookAheadBlend = 1f;

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
    private readonly TimedRotation? _rotation;
    private readonly TimedChannel? _roll;
    private readonly TimedChannel? _fov;
    private readonly float _fovMin;
    private readonly float _fovMax;
    private CarriedUp? _carriedUp;

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
        for (var i = 0; i < _lengths.Length; i++)
            _distances[i + 1] = _distances[i] + _lengths[i];

        var legLengths = new float[track.Points.Count];
        for (var leg = 1; leg < legLengths.Length; leg++)
            legLengths[leg] = _lengths[leg - 1];
        _keys = TimingCompiler.Compile(track, legLengths);
        _distanceKeys = _keys.Select(ToDistance).ToArray();
        _curve = new TimingCurve(_distanceKeys);
        _yaws = TrackAim.UnwrapAngles(track.Points.Select(p => p.Yaw).ToArray());
        _pitches = track.Points.Select(p => p.Pitch).ToArray();
        var fovs = track.Points.Select(p => p.Fov).ToArray();
        _fovMin = fovs.Length == 0 ? 0f : fovs.Min();
        _fovMax = fovs.Length == 0 ? 0f : fovs.Max();
        if (track.Points.Count == 0)
            return;

        // Each point is reached at its key's time and left at its hold end's, or at once.
        var arrive = Enumerable.Range(0, track.Points.Count).Select(PointSeconds).ToArray();
        var depart = arrive
            .Select((at, i) => track.Timing[i].Hold > 0f ? _keys[TrackEditing.PointKey(track, i) + 1].Time : at)
            .ToArray();
        var rolls = TrackAim.UnwrapAngles(track.Points.Select(p => p.Roll).ToArray());
        _rotation = new TimedRotation(
            _yaws.Select((yaw, i) => CameraRotation.FromAngles(yaw, _pitches[i], rolls[i])).ToArray(),
            arrive,
            depart
        );
        _roll = new TimedChannel(rolls, arrive, depart);
        _fov = new TimedChannel(fovs, arrive, depart);
    }

    /// <summary>Leg <paramref name="leg"/>'s length as timing measures it.</summary>
    public float LegLength(int leg)
    {
        TrackEditing.ValidateLegIndex(_track, leg);
        return _lengths[leg - 1];
    }

    /// <summary>The time leg <paramref name="leg"/> takes, from its start key to its end key.</summary>
    public float LegSeconds(int leg) =>
        _keys[TrackEditing.LegEndKey(_track, leg)].Time - _keys[TrackEditing.LegStartKey(_track, leg)].Time;

    /// <summary>The time point <paramref name="point"/> is reached.</summary>
    public float PointSeconds(int point) => _keys[TrackEditing.PointKey(_track, point)].Time;

    /// <summary>The leg whose time span holds <paramref name="time"/>, or null in a hold or outside the shot.</summary>
    public int? LegAt(float time)
    {
        for (var leg = 1; leg < _track.Points.Count; leg++)
        {
            if (
                time >= _keys[TrackEditing.LegStartKey(_track, leg)].Time
                && time <= _keys[TrackEditing.LegEndKey(_track, leg)].Time
            )
                return leg;
        }

        return null;
    }

    /// <summary>The camera's state at <paramref name="time"/>, aimed at <paramref name="target"/> when given, or null for a track with no points.</summary>
    public CameraState? Evaluate(double time, Vector3? target = null)
    {
        if (_track.Points.Count == 0)
            return null;

        if (_track.Points.Count == 1)
        {
            var only = _track.Points[0];
            var (onlyYaw, onlyPitch) = Toward(only.Position, target) ?? (only.Yaw, only.Pitch);
            return CameraState.FromAngles(only.Position, onlyYaw, onlyPitch, only.Roll, only.Fov);
        }

        var (cameraPosition, segment, fraction) = PlaceAt(time);
        var fov = Math.Clamp(_fov!.At(time), _fovMin, _fovMax);
        if (Toward(cameraPosition, target) is { } toward)
            return CameraState.FromAngles(cameraPosition, toward.Yaw, toward.Pitch, _roll!.At(time), fov);

        return _track.Aim == AimMode.PathTangent
            ? Travel(time, cameraPosition, segment, fraction, fov)
            : CameraState.FromRotation(cameraPosition, _rotation!.At(time), fov);
    }

    /// <summary>The aim at <paramref name="target"/> from <paramref name="from"/>, or null with no target or one on the camera.</summary>
    private static (float Yaw, float Pitch)? Toward(Vector3 from, Vector3? target) =>
        target is { } at ? TrackAim.Toward(from, at) : null;

    /// <summary>Distance along the path at <paramref name="time"/>.</summary>
    public float DistanceAt(double time) => _curve.PositionAt(time);

    /// <summary>Speed along the path at <paramref name="time"/>, in distance per second.</summary>
    public float SlopeAt(double time) => _curve.SlopeAt(time);

    /// <summary>The resolved slope on one side of timing key <paramref name="key"/>, in distance per second.</summary>
    public float SideSlope(int key, KeySide side) => _curve.SideSlope(key, side);

    /// <summary>Distance along the path of a place in control-point units.</summary>
    public float DistanceOf(float position)
    {
        if (_lengths.Length == 0)
            return 0f;
        var clamped = Math.Clamp(position, 0f, _lengths.Length);
        var segment = Math.Min((int)MathF.Floor(clamped), _lengths.Length - 1);
        return _distances[segment] + ((clamped - segment) * _lengths[segment]);
    }

    /// <summary>The place in control-point units at <paramref name="distance"/> along the path.</summary>
    public float PositionOf(float distance)
    {
        if (_lengths.Length == 0)
            return 0f;
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
        if (start < 0 || start + 1 >= _distanceKeys.Length)
            return 0f;
        var a = _distanceKeys[start];
        var b = _distanceKeys[start + 1];
        return (b.Position - a.Position) / (b.Time - a.Time);
    }

    /// <summary>The key with its position moved from control-point units to distance along the path.</summary>
    private TimingKey ToDistance(TimingKey key) =>
        _lengths.Length == 0 ? key : key with { Position = DistanceOf(key.Position) };

    /// <summary>Splits a distance along the path into a segment index and the arc fraction into it.</summary>
    private (int Segment, float Fraction) LocateDistance(float distance)
    {
        var last = _lengths.Length - 1;
        if (distance >= _distances[^1])
            return (last, 1f);
        if (distance <= 0f)
            return (0, 0f);

        var lo = Search.LastAtOrBelow(_distances, distance, 0, _lengths.Length);

        return (lo, Math.Clamp((distance - _distances[lo]) / _lengths[lo], 0f, 1f));
    }

    /// <summary>The Direction of travel frame at <paramref name="time"/>: facing along the path or its look ahead, with up carried along it and the track's roll on top.</summary>
    private CameraState Travel(double time, Vector3 from, int segment, float fraction, float fov)
    {
        if (TravelDirection(time, from, segment, fraction) is not { } direction)
            return CameraState.FromAngles(from, _yaws[0], _pitches[0], _roll!.At(time), fov);

        _carriedUp ??= CarriedUp.Along(
            TravelDirection,
            DistanceAt,
            Duration,
            allowInverted: true,
            CameraRotation.Up(CameraRotation.FromAngles(_yaws[0], MathF.PI / 2f, 0f))
        );
        var forward = Vector3.Normalize(direction);
        var up = _carriedUp.At(time, direction, DistanceAt(time));
        var roll = _roll!.At(time);
        if (roll != 0f)
            up = Vector3.Transform(up, Quaternion.CreateFromAxisAngle(forward, roll));
        return new CameraState(from, from + (forward * FreeCamMotion.LookAtDistance), up, fov);
    }

    /// <summary>The Direction of travel direction at <paramref name="time"/>, unclamped: the look-ahead, else the path's own; null where the path has none.</summary>
    private Vector3? TravelDirection(double time, Vector3 from, int segment, float fraction) =>
        LookAhead(time, from) ?? TrackAim.PathDirection(_positions, _table, segment, fraction);

    /// <summary>The Direction of travel direction at <paramref name="time"/>, unclamped; null where the path has none.</summary>
    private Vector3? TravelDirection(double time)
    {
        var (position, segment, fraction) = PlaceAt(time);
        return TravelDirection(time, position, segment, fraction);
    }

    /// <summary>The direction from <paramref name="from"/> to where the path is the track's look-ahead later, the end once past it, blending towards the path's direction into that spot as it nears; null with no look-ahead or no direction.</summary>
    private Vector3? LookAhead(double time, Vector3 from)
    {
        if (_track.LookAhead <= 0f)
            return null;

        var ahead = _curve.PositionAt(time + _track.LookAhead);
        var chord = PointAt(ahead) - from;
        var weight = MathF.Max(0f, ahead - _curve.PositionAt(time)) / LookAheadBlend;
        if (weight >= 1f)
            return TrackAim.Usable(chord);

        // Weighed by distance along the path, a chord shrunk to rounding noise carries almost no weight, and a hairpin's short chord keeps its full weight.
        var start = MathF.Max(0f, ahead - LookAheadBlend);
        var arrival = PointAt(MathF.Min(_distances[^1], start + LookAheadBlend)) - PointAt(start);
        if (arrival.LengthSquared() == 0f)
            return null;
        var toward = chord.LengthSquared() == 0f ? Vector3.Zero : Vector3.Normalize(chord);
        return TrackAim.Usable((weight * toward) + ((1f - weight) * Vector3.Normalize(arrival)));
    }

    /// <summary>Where the camera is on the path at <paramref name="time"/>, and the segment and arc fraction it's in.</summary>
    private (Vector3 Position, int Segment, float Fraction) PlaceAt(double time)
    {
        var (segment, fraction) = LocateDistance(_curve.PositionAt(time));
        return (PointAt(segment, fraction), segment, fraction);
    }

    /// <summary>The place on the path <paramref name="distance"/> along it.</summary>
    private Vector3 PointAt(float distance)
    {
        var (segment, fraction) = LocateDistance(distance);
        return PointAt(segment, fraction);
    }

    private Vector3 PointAt(int segment, float fraction) =>
        CatmullRom.Evaluate(_positions, segment, _table.ParameterAt(segment, fraction));
}
