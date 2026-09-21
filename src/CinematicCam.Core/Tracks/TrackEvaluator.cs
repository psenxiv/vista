using System.Numerics;
using CinematicCam.Core.Camera;

namespace CinematicCam.Core.Tracks;

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
    private readonly TimingCurve _curve;
    private readonly float[] _yaws;
    private readonly float[] _pitches;
    private readonly float[] _rolls;
    private readonly float[] _fovs;
    private readonly float _fovMin;
    private readonly float _fovMax;

    /// <summary>Total shot length: the timing curve's last key, 0 with no keys.</summary>
    public double Duration => _curve.Duration;

    /// <summary>Builds the spline, arc-length table, distance timing curve and unwrapped yaw once for <paramref name="track"/>.</summary>
    public TrackEvaluator(Track track)
    {
        _track = track;
        _positions = track.Points.Select(p => p.Position).ToArray();
        _table = new ArcLengthTable(_positions);
        _lengths = new float[_table.SegmentCount];
        _distances = new float[_table.SegmentCount + 1];
        for (var i = 0; i < _table.SegmentCount; i++)
        {
            _lengths[i] = MathF.Max(_table.SegmentLength(i), MinTimingLength);
            _distances[i + 1] = _distances[i] + _lengths[i];
        }

        _curve = new TimingCurve(track.Timing.Select(ToDistance).ToArray());
        _yaws = TrackAim.UnwrapAngles(track.Points.Select(p => p.Yaw).ToArray());
        _pitches = track.Points.Select(p => p.Pitch).ToArray();
        _rolls = TrackAim.UnwrapAngles(track.Points.Select(p => p.Roll).ToArray());
        _fovs = track.Points.Select(p => p.Fov).ToArray();
        _fovMin = _fovs.Length == 0 ? 0f : _fovs.Min();
        _fovMax = _fovs.Length == 0 ? 0f : _fovs.Max();
    }

    /// <summary>The camera's state at <paramref name="time"/>, or null for a track with no points.</summary>
    public CameraState? Evaluate(double time)
    {
        if (_track.Points.Count == 0) return null;

        if (_track.Points.Count == 1)
        {
            var only = _track.Points[0];
            return new CameraState(only.Position, FreeCamMotion.LookAtFrom(only.Position, only.Yaw, only.Pitch), only.Fov, only.Roll);
        }

        var (segment, fraction) = LocateDistance(_curve.PositionAt(time));
        var parameter = _table.ParameterAt(segment, fraction);
        var cameraPosition = CatmullRom.Evaluate(_positions, segment, parameter);

        var (yaw, pitch) = _track.Aim == AimMode.AimKeys
            ? AimKeys(segment, fraction)
            : TrackAim.PathTangent(_positions, _table, segment, fraction, (_yaws[0], _pitches[0]));

        var fov = Math.Clamp(TrackAim.Channel(_fovs, segment, fraction), _fovMin, _fovMax);
        var roll = TrackAim.Channel(_rolls, segment, fraction);

        return new CameraState(cameraPosition, FreeCamMotion.LookAtFrom(cameraPosition, yaw, pitch), fov, roll);
    }

    /// <summary>The key with its position and tangents moved from control-point units to distance along the path.</summary>
    private TimingKey ToDistance(TimingKey key)
    {
        if (_lengths.Length == 0) return key;

        var position = Math.Clamp(key.Position, 0f, _lengths.Length);
        var segment = Math.Min((int)MathF.Floor(position), _lengths.Length - 1);
        var fraction = position - segment;
        var inSegment = fraction == 0f && segment > 0 ? segment - 1 : segment;

        return key with
        {
            Position = _distances[segment] + (fraction * _lengths[segment]),
            InTangent = key.InTangent * _lengths[inSegment],
            OutTangent = key.OutTangent * _lengths[segment],
        };
    }

    /// <summary>Splits a distance along the path into a segment index and the arc fraction into it.</summary>
    private (int Segment, float Fraction) LocateDistance(float distance)
    {
        var last = _lengths.Length - 1;
        if (distance >= _distances[^1]) return (last, 1f);
        if (distance <= 0f) return (0, 0f);

        var lo = 0;
        var hi = _lengths.Length;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_distances[mid] <= distance) lo = mid; else hi = mid;
        }

        return (lo, Math.Clamp((distance - _distances[lo]) / _lengths[lo], 0f, 1f));
    }

    private (float Yaw, float Pitch) AimKeys(int segment, float fraction)
    {
        var yaw = TrackAim.Channel(_yaws, segment, fraction);
        var pitch = Math.Clamp(TrackAim.Channel(_pitches, segment, fraction), -TrackAim.PitchLimit, TrackAim.PitchLimit);
        return (yaw, pitch);
    }
}
