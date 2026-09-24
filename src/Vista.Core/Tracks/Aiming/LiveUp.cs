using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A live aim's up, which can't see a vertical passage coming: carried from frame to frame with the facing, and settled back toward its target no faster than <see cref="SettleRate"/>.</summary>
public static class LiveUp
{
    /// <summary>The fastest the picture turns about its own centre to settle, in radians a second: 180°, half the spin limit.</summary>
    public const float SettleRate = MathF.PI;

    /// <summary>A facing that turns more than this in a frame has snapped round, so up turns about itself instead of being carried.</summary>
    private const float SnapAngle = MathF.PI / 2f;

    /// <summary>Within this of a half turn from its target, 10°, settling keeps turning the way it last turned rather than the way that's shorter, which float noise flips at a half turn.</summary>
    private const float TieMargin = 10f * MathF.PI / 180f;

    /// <summary>From a vertical passage's edge (<see cref="LevelUp.PassageSideways"/>) out to 30° from straight up or down, settling fades back in; inside a passage it doesn't settle at all.</summary>
    private static readonly float PoleFade = MathF.Sin(30f * MathF.PI / 180f);

    /// <summary>The up carried from facing <paramref name="from"/> to facing <paramref name="to"/>: turned by as much as the facing turns, or, past a snap, turned about itself.</summary>
    public static Vector3 Carry(Vector3 from, Vector3 to, Vector3 up)
    {
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to)), -1f, 1f));
        var carried = angle > SnapAngle ? up : Vector3.Transform(up, CameraRotation.MinimalRotation(from, to));
        return CameraRotation.SquareUp(carried, Vector3.Normalize(to));
    }

    /// <summary>The up turned about unit <paramref name="forward"/> toward <paramref name="target"/> for <paramref name="seconds"/>, no faster than <see cref="SettleRate"/> and fading near straight up or down, and the way it turned (+1 or -1): near a half turn it keeps going <paramref name="way"/>, the way it last turned.</summary>
    public static (Vector3 Up, float Way) SettleToward(
        Vector3 up,
        Vector3 forward,
        Vector3 target,
        float seconds,
        float way
    )
    {
        var fade = Math.Clamp(
            (CameraRotation.Sideways(forward) - LevelUp.PassageSideways) / (PoleFade - LevelUp.PassageSideways),
            0f,
            1f
        );
        if (fade <= 0f || seconds <= 0f)
            return (up, way);
        var angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(up, target), forward), Vector3.Dot(up, target));
        if (angle * way < -(MathF.PI - TieMargin))
            angle += 2f * MathF.PI * way;
        var most = SettleRate * seconds * fade;
        var turn = Math.Clamp(angle, -most, most);
        if (turn == 0f)
            return (up, way);
        return (
            CameraRotation.SquareUp(Vector3.Transform(up, Quaternion.CreateFromAxisAngle(forward, turn)), forward),
            MathF.Sign(turn)
        );
    }
}
