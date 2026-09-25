using System.Globalization;
using System.Numerics;
using Vista.Core.Editing;

namespace Vista.Core.Camera;

/// <summary>The rules every frame Vista writes must keep: finite, looking at least 1 cm ahead, a unit up square to the view, and a field of view the editor allows.</summary>
public static class WellFormed
{
    /// <summary>The closest a well-formed frame's look-at may be to its position, in yalms: 1 cm.</summary>
    public const float MinLookAtDistance = 0.01f;

    /// <summary>How far a well-formed frame's up may be from unit length, and its dot with the unit view from 0.</summary>
    public const float UpTolerance = 1e-3f;

    /// <summary>The first rule <paramref name="frame"/> breaks, with the value that broke it, or null when it's well-formed.</summary>
    public static string? FirstBroken(CameraState frame)
    {
        if (!IsFinite(frame.Position))
            return Broken("the position isn't finite", frame.Position);
        if (!IsFinite(frame.LookAt))
            return Broken("the look-at isn't finite", frame.LookAt);
        if (!IsFinite(frame.Up))
            return Broken("the up isn't finite", frame.Up);
        if (!float.IsFinite(frame.Fov))
            return Broken("the field of view isn't finite", frame.Fov);
        var view = frame.LookAt - frame.Position;
        if (!(view.Length() >= MinLookAtDistance))
            return Broken("the look-at is too close to the position", view.Length());
        if (!(MathF.Abs(frame.Up.Length() - 1f) <= UpTolerance))
            return Broken("the up isn't unit length", frame.Up.Length());
        var square = Vector3.Dot(frame.Up, Vector3.Normalize(view));
        if (!(MathF.Abs(square) <= UpTolerance))
            return Broken("the up isn't square to the view", square);
        if (!(frame.Fov >= EditLimits.MinFov && frame.Fov <= EditLimits.MaxFov))
            return Broken("the field of view is out of range", frame.Fov);
        return null;
    }

    private static string Broken(string rule, object value) =>
        string.Create(CultureInfo.InvariantCulture, $"{rule} ({value})");

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
