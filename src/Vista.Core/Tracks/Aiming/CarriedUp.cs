using System.Numerics;
using Vista.Core.Camera;

namespace Vista.Core.Tracks.Aiming;

/// <summary>A camera's up kept level, upright or inverted, carried along with its facing through straight up or down and turned back to level no faster than <see cref="SettleRate"/>: worked out once along a shot and read back at any time.</summary>
public sealed class CarriedUp
{
    /// <summary>The fastest the picture turns about its own centre to settle level, in radians a second: 180°, half the spin limit.</summary>
    public const float SettleRate = MathF.PI;

    /// <summary>Seconds between the samples the up is carried through.</summary>
    public const double StepSeconds = 0.01;

    /// <summary>A facing that turns more than this between samples has snapped round, so up turns about itself instead of being carried.</summary>
    private const float SnapAngle = MathF.PI / 2f;

    /// <summary>How far past a quarter turn from level, as a dot product, up must go before level switches between upright and inverted, so it can't flicker at a quarter turn.</summary>
    private const float SwitchMargin = 0.2f;

    /// <summary>Within 10° of straight up or down (a sideways part shorter than sin 10°), settling fades to nothing, since level swings round fast there and is undefined at the pole.</summary>
    private static readonly float PoleFade = MathF.Sin(10f * MathF.PI / 180f);

    private readonly Vector3[] ups;
    private readonly Vector3[] facings;
    private readonly float[] distances;
    private readonly bool[] inverted;

    private CarriedUp(Vector3[] ups, Vector3[] facings, float[] distances, bool[] inverted)
    {
        this.ups = ups;
        this.facings = facings;
        this.distances = distances;
        this.inverted = inverted;
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
        var sampleInverted = new bool[count];
        Vector3? previous = null;
        var up = Vector3.UnitY;
        var distance = 0f;
        var upsideDown = false;
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
                {
                    var dot = Vector3.Dot(up, CameraRotation.Upright(forward));
                    if (allowInverted && (upsideDown ? dot > SwitchMargin : dot < -SwitchMargin))
                        upsideDown = !upsideDown;
                    up = SettleLevel(up, forward, (float)StepSeconds, upsideDown);
                }
            }

            samples[k] = up;
            sampleFacings[k] = forward;
            sampleDistances[k] = moved;
            sampleInverted[k] = upsideDown;
            previous = forward;
            distance = moved;
        }

        return new CarriedUp(samples, sampleFacings, sampleDistances, sampleInverted);
    }

    /// <summary>The up at <paramref name="time"/>, facing <paramref name="facing"/> having <paramref name="travelled"/>: carried on from the sample before, and settled for the share of that step's travel made so far, so it's still while the camera holds.</summary>
    public Vector3 At(double time, Vector3 facing, float travelled)
    {
        var index = Math.Clamp((int)(time / StepSeconds), 0, ups.Length - 1);
        var forward = Vector3.Normalize(facing);
        var up = forward == facings[index] ? ups[index] : Carry(facings[index], forward, ups[index]);
        // Settles as the table did on to the next sample: only if the camera travels between them, by the share so far.
        if (index + 1 >= ups.Length || distances[index + 1] == distances[index] || travelled == distances[index])
            return up;
        var share = Math.Clamp((travelled - distances[index]) / (distances[index + 1] - distances[index]), 0f, 1f);
        return SettleLevel(up, forward, (float)StepSeconds * share, inverted[index + 1]);
    }

    /// <summary>The up carried from facing <paramref name="from"/> to facing <paramref name="to"/>: turned by as much as the facing turns, or, past a snap, turned about itself.</summary>
    public static Vector3 Carry(Vector3 from, Vector3 to, Vector3 up)
    {
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to)), -1f, 1f));
        var carried = angle > SnapAngle ? up : Vector3.Transform(up, CameraRotation.MinimalRotation(from, to));
        return Square(carried, Vector3.Normalize(to));
    }

    /// <summary>The up turned about <paramref name="facing"/> toward level for <paramref name="seconds"/>, no faster than <see cref="SettleRate"/>: toward upright, or toward inverted when <paramref name="allowInverted"/> and it's nearer.</summary>
    public static Vector3 Settle(Vector3 up, Vector3 facing, float seconds, bool allowInverted)
    {
        var forward = Vector3.Normalize(facing);
        return SettleLevel(
            up,
            forward,
            seconds,
            allowInverted && Vector3.Dot(up, CameraRotation.Upright(forward)) < 0f
        );
    }

    /// <summary>The up turned about unit <paramref name="forward"/> toward upright, or inverted when <paramref name="upsideDown"/>, for <paramref name="seconds"/>.</summary>
    private static Vector3 SettleLevel(Vector3 up, Vector3 forward, float seconds, bool upsideDown)
    {
        var target = CameraRotation.Upright(forward);
        return SettleToward(up, forward, upsideDown ? -target : target, seconds);
    }

    /// <summary>The up turned about unit <paramref name="forward"/> toward <paramref name="target"/> for <paramref name="seconds"/>, no faster than <see cref="SettleRate"/> and fading within <see cref="PoleFade"/> of straight up or down; a half turn goes the positive way.</summary>
    public static Vector3 SettleToward(Vector3 up, Vector3 forward, Vector3 target, float seconds)
    {
        var fade = MathF.Min(MathF.Sqrt((forward.X * forward.X) + (forward.Z * forward.Z)) / PoleFade, 1f);
        if (fade <= 0f)
            return up;
        var angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(up, target), forward), Vector3.Dot(up, target));
        var most = SettleRate * seconds * fade;
        var turn = Math.Clamp(angle, -most, most);
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
