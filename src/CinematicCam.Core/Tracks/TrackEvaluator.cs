using System.Numerics;

namespace CinematicCam.Core;

/// <summary>Turns a track and a moment in time into where the camera is, looks and its field of view.</summary>
public sealed class TrackEvaluator
{
    private readonly Track _track;
    private readonly Vector3[] _positions;
    private readonly ArcLengthTable _table;
    private readonly TimingCurve _curve;
    private readonly float[] _yaws;
    private readonly float[] _pitches;
    private readonly float[] _fovs;
    private readonly float _fovMin;
    private readonly float _fovMax;

    /// <summary>Total shot length: the timing curve's last key, 0 with no keys.</summary>
    public double Duration => _curve.Duration;

    /// <summary>Builds the spline, arc-length table, timing curve and unwrapped yaw once for <paramref name="track"/>.</summary>
    public TrackEvaluator(Track track)
    {
        _track = track;
        _positions = track.Points.Select(p => p.Position).ToArray();
        _table = new ArcLengthTable(_positions);
        _curve = new TimingCurve(track.Timing);
        _yaws = TrackAim.UnwrapYaw(track.Points.Select(p => p.Yaw).ToArray());
        _pitches = track.Points.Select(p => p.Pitch).ToArray();
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
            return new CameraState(only.Position, FreeCamMotion.LookAtFrom(only.Position, only.Yaw, only.Pitch), only.Fov);
        }

        var position = _curve.PositionAt(time);
        var (segment, fraction) = _table.Locate(position);
        var parameter = _table.ParameterAt(segment, fraction);
        var cameraPosition = CatmullRom.Evaluate(_positions, segment, parameter);

        var (yaw, pitch) = _track.Aim == AimMode.AimKeys
            ? AimKeys(segment, fraction)
            : TrackAim.PathTangent(_positions, _table, segment, fraction, (_yaws[0], _pitches[0]));

        var fov = Math.Clamp(TrackAim.Channel(_fovs, segment, fraction), _fovMin, _fovMax);

        return new CameraState(cameraPosition, FreeCamMotion.LookAtFrom(cameraPosition, yaw, pitch), fov);
    }

    private (float Yaw, float Pitch) AimKeys(int segment, float fraction)
    {
        var yaw = TrackAim.Channel(_yaws, segment, fraction);
        var pitch = Math.Clamp(TrackAim.Channel(_pitches, segment, fraction), -TrackAim.PitchLimit, TrackAim.PitchLimit);
        return (yaw, pitch);
    }
}
