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

    /// <summary>Seconds between the samples that look for vertical stretches.</summary>
    private const double StretchScanStep = 0.01;

    /// <summary>Seconds between the samples that look outward from a steep moment for the stretch the scan missed.</summary>
    private const double StretchSearchStep = 0.0005;

    /// <summary>Times each vertical stretch's edge is halved, to about a millionth of the scan step.</summary>
    private const int CrossingHalvings = 20;

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
    private List<Stretch>? _stretches;

    /// <summary>A span where Direction of travel is steeper than the pitch limit, with the yaw at each edge; null where the track starts or ends inside it.</summary>
    private readonly record struct Stretch(double From, double To, float? FromYaw, float? ToYaw);

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

        var (yaw, pitch) =
            Toward(cameraPosition, target)
            ?? (_track.Aim == AimMode.PathTangent ? Travel(time, cameraPosition, segment, fraction) : AimKeys(time));

        var fov = Math.Clamp(_fov!.At(time), _fovMin, _fovMax);
        var roll = _roll!.At(time);

        return CameraState.FromAngles(cameraPosition, yaw, pitch, roll, fov);
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

    private (float Yaw, float Pitch) AimKeys(double time) =>
        (_yaw!.At(time), Math.Clamp(_pitch!.At(time), -TrackAim.PitchLimit, TrackAim.PitchLimit));

    /// <summary>The Direction of travel aim at <paramref name="time"/>, turning evenly through a vertical stretch.</summary>
    private (float Yaw, float Pitch) Travel(double time, Vector3 from, int segment, float fraction)
    {
        if (TravelDirection(time, from, segment, fraction) is not { } direction)
            return (_yaws[0], _pitches[0]);

        var aim = TrackAim.Along(direction);
        return IsSteep(direction) ? (YawThroughVertical(time), aim.Pitch) : aim;
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

    /// <summary>Whether <paramref name="direction"/> is steeper than the pitch limit.</summary>
    private static bool IsSteep(Vector3 direction) =>
        MathF.Abs(TrackAim.FromDirection(direction).Pitch) > TrackAim.PitchLimit;

    /// <summary>Whether Direction of travel at <paramref name="time"/> is steeper than the pitch limit.</summary>
    private bool IsSteep(double time) => TravelDirection(time) is { } direction && IsSteep(direction);

    /// <summary>The yaw at steep <paramref name="time"/>, turned evenly with distance the short way between its stretch's edges.</summary>
    private float YawThroughVertical(double time)
    {
        _stretches ??= FindStretches();
        var known = _stretches.FindIndex(s => Covers(s, time));
        var stretch = known >= 0 ? _stretches[known] : StretchAround(time);

        return (stretch.FromYaw, stretch.ToYaw) switch
        {
            ({ } start, { } end) => start + (Angles.Delta(start, end) * Travelled(stretch, time)),
            ({ } start, null) => start,
            (null, { } end) => end,
            _ => _yaws[0],
        };
    }

    /// <summary>Whether <paramref name="time"/> falls inside <paramref name="stretch"/>, which runs to the track's start or end on a side with no edge yaw.</summary>
    private static bool Covers(Stretch stretch, double time) =>
        (stretch.FromYaw is null || time >= stretch.From) && (stretch.ToYaw is null || time <= stretch.To);

    /// <summary>How far through <paramref name="stretch"/> <paramref name="time"/> is, by distance along the path, or by time where it covers none.</summary>
    private float Travelled(Stretch stretch, double time)
    {
        var from = DistanceAt(stretch.From);
        var to = DistanceAt(stretch.To);
        if (to != from)
            return (DistanceAt(time) - from) / (to - from);
        return stretch.To > stretch.From ? (float)((time - stretch.From) / (stretch.To - stretch.From)) : 0f;
    }

    /// <summary>The stretch around steep <paramref name="time"/> that the scan missed, searched for outward and kept.</summary>
    private Stretch StretchAround(double time)
    {
        var from = Edge(time, -StretchSearchStep);
        var to = Edge(time, StretchSearchStep);
        var stretch = new Stretch(from.Time, to.Time, from.Yaw, to.Yaw);
        _stretches!.Add(stretch);
        return stretch;
    }

    /// <summary>The edge of the stretch around steep <paramref name="time"/>, stepping by <paramref name="step"/> until the aim isn't steep, with its yaw; null yaw where the track starts or ends first.</summary>
    private (double Time, float? Yaw) Edge(double time, double step)
    {
        var inside = time;
        while (true)
        {
            var outside = Math.Clamp(inside + step, 0.0, Duration);
            if (!IsSteep(outside))
                return Crossing(outside, inside);
            if (outside == inside)
                return (outside, null);
            inside = outside;
        }
    }

    /// <summary>Every vertical stretch in the track, sampled every <see cref="StretchScanStep"/> and at the end.</summary>
    private List<Stretch> FindStretches()
    {
        var stretches = new List<Stretch>();
        var samples = (int)Math.Ceiling(Duration / StretchScanStep);
        (double Time, float? Yaw)? opened = null;
        var previous = 0.0;
        for (var i = 0; i <= samples; i++)
        {
            var time = Math.Min(i * StretchScanStep, Duration);
            var steep = IsSteep(time);
            if (steep && opened is null)
                opened = i == 0 ? (0.0, null) : Crossing(previous, time);
            else if (!steep && opened is { } start)
            {
                var end = Crossing(time, previous);
                stretches.Add(new Stretch(start.Time, end.Time, start.Yaw, end.Yaw));
                opened = null;
            }

            previous = time;
        }

        if (opened is { } last)
            stretches.Add(new Stretch(last.Time, Duration, last.Yaw, null));
        return stretches;
    }

    /// <summary>The time within a hair of the vertical between <paramref name="outside"/> and steep <paramref name="inside"/>, on the outside, with its yaw.</summary>
    private (double Time, float Yaw) Crossing(double outside, double inside)
    {
        for (var i = 0; i < CrossingHalvings; i++)
        {
            var middle = (outside + inside) / 2;
            if (IsSteep(middle))
                inside = middle;
            else
                outside = middle;
        }

        return (outside, TrackAim.FromDirection(TravelDirection(outside)!.Value).Yaw);
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
