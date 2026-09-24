using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A camera's up carried along with its facing like a rollercoaster car's, settling toward level while the facing is level: worked out once along a shot and read back at any time.</summary>
public sealed class CarriedUp
{
    /// <summary>The time constant, in seconds, of settling toward level.</summary>
    public const float SettleSeconds = 1f;

    /// <summary>Seconds between the samples the up is carried through.</summary>
    public const double StepSeconds = 0.01;

    /// <summary>A facing that turns more than this between samples has snapped round, so up turns about itself instead of being carried.</summary>
    private const float SnapAngle = MathF.PI / 2f;

    private readonly Vector3[] ups;
    private readonly Vector3[] facings;
    private readonly float[] distances;
    private readonly bool allowInverted;

    private CarriedUp(Vector3[] ups, Vector3[] facings, float[] distances, bool allowInverted)
    {
        this.ups = ups;
        this.facings = facings;
        this.distances = distances;
        this.allowInverted = allowInverted;
    }

    /// <summary>The up along a shot of <paramref name="duration"/> seconds facing <paramref name="facing"/>, settling only while <paramref name="travelled"/> changes, so a hold stays still: upright at the start, or <paramref name="verticalStartUp"/> (negated facing down) if it starts facing straight up or down, and settling toward inverted too when <paramref name="allowInverted"/>.</summary>
    public static CarriedUp Along(
        Func<double, Vector3?> facing,
        Func<double, float> travelled,
        double duration,
        bool allowInverted,
        Vector3 verticalStartUp
    )
    {
        var count = (int)Math.Ceiling(duration / StepSeconds) + 1;
        var samples = new Vector3[count];
        var sampleFacings = new Vector3[count];
        var sampleDistances = new float[count];
        Vector3? previous = null;
        var up = Vector3.UnitY;
        var distance = 0f;
        for (var k = 0; k < count; k++)
        {
            var time = Math.Min(k * StepSeconds, duration);
            var forward =
                facing(time) is { } f && f != Vector3.Zero
                    ? Vector3.Normalize(f)
                    : previous ?? new Vector3(0f, 0f, -1f);
            var moved = travelled(time);
            if (previous is not { } last)
                up = Start(forward, forward.Y < 0f ? -verticalStartUp : verticalStartUp);
            else
            {
                // An unchanged facing keeps its up exactly, and settling waits for travel, so a hold stays still.
                if (forward != last)
                    up = Carry(last, forward, up);
                if (moved != distance)
                    up = Settle(up, forward, (float)StepSeconds, allowInverted);
            }

            samples[k] = up;
            sampleFacings[k] = forward;
            sampleDistances[k] = moved;
            previous = forward;
            distance = moved;
        }

        return new CarriedUp(samples, sampleFacings, sampleDistances, allowInverted);
    }

    /// <summary>The up at <paramref name="time"/>, facing <paramref name="facing"/> having <paramref name="travelled"/>: carried on from the sample before, and settled for the share of that step's travel made so far, so it's still while the camera holds.</summary>
    public Vector3 At(double time, Vector3 facing, float travelled)
    {
        var index = Math.Clamp((int)(time / StepSeconds), 0, ups.Length - 1);
        var forward = Vector3.Normalize(facing);
        var up = forward == facings[index] ? ups[index] : Carry(facings[index], forward, ups[index]);
        if (index + 1 >= ups.Length || travelled == distances[index])
            return up;
        var share = (travelled - distances[index]) / (distances[index + 1] - distances[index]);
        return Settle(up, forward, (float)StepSeconds * share, allowInverted);
    }

    /// <summary>The up carried from facing <paramref name="from"/> to facing <paramref name="to"/>: turned by as much as the facing turns, or, past a snap, turned about itself.</summary>
    public static Vector3 Carry(Vector3 from, Vector3 to, Vector3 up)
    {
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to)), -1f, 1f));
        var carried = angle > SnapAngle ? up : Vector3.Transform(up, CameraRotation.MinimalRotation(from, to));
        return Square(carried, Vector3.Normalize(to));
    }

    /// <summary>The up turned about <paramref name="facing"/> toward level for <paramref name="seconds"/>: toward upright, or toward inverted when <paramref name="allowInverted"/> and it's nearer; less the steeper the facing, and not at all facing straight up or down.</summary>
    public static Vector3 Settle(Vector3 up, Vector3 facing, float seconds, bool allowInverted)
    {
        var forward = Vector3.Normalize(facing);
        var level = 1f - (forward.Y * forward.Y);
        if (level <= 0f)
            return up;
        var target = CameraRotation.Upright(forward);
        if (allowInverted && Vector3.Dot(up, target) < 0f)
            target = -target;

        // Signed angle from up to the target about the facing; a half turn goes the positive way.
        var angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(up, target), forward), Vector3.Dot(up, target));
        var turn = angle * (1f - MathF.Exp(-seconds / SettleSeconds)) * level;
        return Square(Vector3.Transform(up, Quaternion.CreateFromAxisAngle(forward, turn)), forward);
    }

    /// <summary>The first up: upright, or <paramref name="verticalStartUp"/> when facing straight up or down.</summary>
    private static Vector3 Start(Vector3 forward, Vector3 verticalStartUp)
    {
        var world = Vector3.UnitY - (forward * forward.Y);
        return world.LengthSquared() > 1e-12f ? Vector3.Normalize(world) : Square(verticalStartUp, forward);
    }

    /// <summary><paramref name="up"/> made square to unit <paramref name="forward"/> and unit length, or upright if it lies along the forward.</summary>
    private static Vector3 Square(Vector3 up, Vector3 forward)
    {
        var squared = up - (forward * Vector3.Dot(up, forward));
        return squared.LengthSquared() > 1e-12f ? Vector3.Normalize(squared) : CameraRotation.Upright(forward);
    }
}
