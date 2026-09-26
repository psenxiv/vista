using System.Globalization;
using System.Numerics;
using System.Text;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

namespace Vista.Tests.Sweep;

/// <summary>Every path shape played under every aim setting through one set of checks (movement sweep spec §2 to §5).</summary>
public class MovementSweepTests
{
    /// <summary>The environment variable that makes the sweep also write its report.</summary>
    private const string ReportVariable = "VISTA_SWEEP_REPORT";

    /// <summary>What the sweep checks on each combination it applies to.</summary>
    private enum Check
    {
        WellFormed,
        Snaps,
        Whips,
        PointsReached,
        AimReached,
        SpeedContinuous,
        TurnRateContinuous,
        HorizonLevel,
        LookAtCentred,
    }

    /// <summary>A path shape: its name, its points' positions, and the point held <see cref="HoldSeconds"/>, if any.</summary>
    private sealed record Shape(string Name, Vector3[] Positions, int? Held = null);

    /// <summary>An aim setting: its name, and the track it makes through a shape's positions.</summary>
    private sealed record Aim(string Name, Func<Vector3[], Track> Build);

    /// <summary>A shape played under an aim setting.</summary>
    private sealed record Combination(Shape Shape, Aim Aim)
    {
        internal string Name => $"{Shape.Name} / {Aim.Name}";

        /// <summary>The track, built as the editor builds it.</summary>
        internal Track Track()
        {
            var track = Aim.Build(Shape.Positions);
            return Shape.Held is { } held ? TrackEditing.SetHold(track, held, HoldSeconds) : track;
        }
    }

    /// <summary>What a check measured on a combination: a count, or the worst value of a bounded measure, and where it was worst.</summary>
    private sealed record Measure(Check Check, float Value, string Where, IReadOnlyList<PointMeasure>? Points = null);

    /// <summary>What a check measured at one point, and what was seen there.</summary>
    private sealed record PointMeasure(int Point, float Value, string Where);

    /// <summary>A failure a spec implies: the shape, the aim setting (every aim when null), the check, the count it has or the bound it stays within, why, and the spec it follows from.</summary>
    private sealed record Declared(Shape Shape, Aim? Aim, Check Check, float Expected, string Reason, string Spec);

    /// <summary>A failure waiting for the user's triage: the shape, the aim setting (every aim when null), the check, the point it was found at (the whole measure when null, only for a check not measured at each point), the value measured when it was listed, and what's seen. It fails if that value moves further than the check's own limit or falls within the limit, and every other point is held to the limit.</summary>
    private sealed record Pending(Shape Shape, Aim? Aim, Check Check, int? Point, float Measured, string Seen);

    /// <summary>The seconds the middle-hold shape holds its middle point.</summary>
    private const float HoldSeconds = 2f;

    /// <summary>How far each Look At point sits from the place it's measured from, in yalms.</summary>
    private const float LookAtOffset = 10f;

    private static readonly Shape Straight = new("Straight", PathShapes.Straight);
    private static readonly Shape GentleCurve = new("Gentle curve", PathShapes.GentleCurve);
    private static readonly Shape SCurve = new("S-curve", PathShapes.SCurve);
    private static readonly Shape Corner = new("Right-angle corner", PathShapes.Corner);
    private static readonly Shape Hairpin = new("Hairpin", PathShapes.Hairpin);
    private static readonly Shape Doubleback = new("Doubleback", PathShapes.Doubleback);
    private static readonly Shape Loop = new("Vertical loop", PathShapes.Loop);
    private static readonly Shape Crane = new("Crane", PathShapes.Crane);
    private static readonly Shape Spiral = new("Climbing spiral", PathShapes.ClimbingTurn);
    private static readonly Shape Orbit = new("Orbit", PathShapes.Orbit);
    private static readonly Shape Uneven = new("Uneven spacing", PathShapes.UnevenSpacing);
    private static readonly Shape MiddleHold = new("Middle hold", PathShapes.Corner, Held: 1);
    private static readonly Shape ClosePoints = new("Close points", PathShapes.ClosePoints);
    private static readonly Shape UpAndOver = new("Up and over", PathShapes.UpAndOver);

    private static readonly Aim Pan = new("Recorded aim: pan", ps => Recorded(ps, i => (40f * i, 0f, 0f)));
    private static readonly Aim Tilt = new(
        "Recorded aim: tilt",
        ps => Recorded(ps, i => (0f, -30f + (90f * i / (ps.Length - 1)), 0f))
    );
    private static readonly Aim OverTheTop = new("Recorded aim: over the top", ps => Recorded(ps, OverTheTopLook));
    private static readonly Aim WithRoll = new(
        "Recorded aim: with roll",
        ps => Recorded(ps, i => (40f * i, 0f, i % 2 == 0 ? 30f : -30f))
    );
    private static readonly Aim LookAhead0 = new("Direction of travel: look ahead 0", ps => Travel(ps, 0f));
    private static readonly Aim LookAheadHalf = new("Direction of travel: look ahead 0.5", ps => Travel(ps, 0.5f));
    private static readonly Aim LookAhead2 = new("Direction of travel: look ahead 2", ps => Travel(ps, 2f));
    private static readonly Aim Above = new(
        "Look At: above",
        ps => LookAt(ps, Middle(ps) + (Vector3.UnitY * LookAtOffset))
    );
    private static readonly Aim Below = new(
        "Look At: below",
        ps => LookAt(ps, Middle(ps) - (Vector3.UnitY * LookAtOffset))
    );
    private static readonly Aim Beside = new(
        "Look At: beside",
        ps => LookAt(ps, Middle(ps) + (Vector3.UnitZ * LookAtOffset))
    );
    private static readonly Aim OverAPoint = new("Look At: over a point", ps => LookAt(ps, Passed(ps, LookAtOffset)));
    private static readonly Aim UnderAPoint = new(
        "Look At: under a point",
        ps => LookAt(ps, Passed(ps, -LookAtOffset))
    );

    /// <summary>The spec's shapes (§2.1).</summary>
    private static readonly Shape[] Shapes =
    [
        Straight,
        GentleCurve,
        SCurve,
        Corner,
        Hairpin,
        Doubleback,
        Loop,
        Crane,
        Spiral,
        Orbit,
        Uneven,
        MiddleHold,
        ClosePoints,
        UpAndOver,
    ];

    /// <summary>The spec's aim settings (§2.2).</summary>
    private static readonly Aim[] Aims =
    [
        Pan,
        Tilt,
        OverTheTop,
        WithRoll,
        LookAhead0,
        LookAheadHalf,
        LookAhead2,
        Above,
        Below,
        Beside,
        OverAPoint,
        UnderAPoint,
    ];

    /// <summary>Recorded aim through <paramref name="positions"/>, each point's yaw, pitch and roll in degrees from <paramref name="look"/>.</summary>
    private static Track Recorded(Vector3[] positions, Func<int, (float Yaw, float Pitch, float Roll)> look) =>
        TrackThrough(
            positions.Select(
                (p, i) =>
                {
                    var (yaw, pitch, roll) = look(i);
                    return new ControlPoint(p, yaw * Deg, pitch * Deg, PathShapes.Fov, roll * Deg);
                }
            )
        );

    /// <summary>Point <paramref name="point"/>'s look in degrees: 45° up, straight up, then 45° up with yaw turned 180°, straight up again, and so on, so the camera keeps turning over the top the same way.</summary>
    private static (float Yaw, float Pitch, float Roll) OverTheTopLook(int point) =>
        (180f * (point / 2), point % 2 == 1 ? 90f : 45f, 0f);

    /// <summary>Direction of travel through <paramref name="positions"/>, looking <paramref name="lookAhead"/> seconds ahead.</summary>
    private static Track Travel(Vector3[] positions, float lookAhead) =>
        TrackEditing.SetLookAhead(TrackThrough(Unaimed(positions), AimMode.PathTangent), lookAhead);

    /// <summary>Look At through <paramref name="positions"/> at <paramref name="point"/>.</summary>
    private static Track LookAt(Vector3[] positions, Vector3 point) =>
        TrackEditing.SetLookAt(TrackThrough(Unaimed(positions), AimMode.LookAt), point);

    /// <summary>Points at <paramref name="positions"/> with no recorded aim or roll.</summary>
    private static IEnumerable<ControlPoint> Unaimed(Vector3[] positions) =>
        positions.Select(p => new ControlPoint(p, 0f, 0f, PathShapes.Fov));

    /// <summary>The middle of the box round <paramref name="positions"/>.</summary>
    private static Vector3 Middle(Vector3[] positions) =>
        (positions.Aggregate(Vector3.Min) + positions.Aggregate(Vector3.Max)) / 2f;

    /// <summary>The place <paramref name="height"/> yalms over the point nearest the middle of <paramref name="positions"/> for which that place isn't on another of its points: the crane's vertical leg runs through the places over and under its middle point.</summary>
    private static Vector3 Passed(Vector3[] positions, float height)
    {
        var middle = positions.Length / 2;
        return Enumerable
            .Range(0, positions.Length)
            .OrderBy(i => Math.Abs(i - middle))
            .Select(i => positions[i] + (Vector3.UnitY * height))
            .First(place => positions.All(p => Vector3.Distance(p, place) >= 1f));
    }

    private static IEnumerable<Combination> Combinations() =>
        Shapes.SelectMany(shape => Aims.Select(aim => new Combination(shape, aim)));

    /// <summary>How far from a point the camera may be at its arrive time, in yalms. The timing curve gives the point's own distance along the path there, the table its segment's end and the spline the point itself, each rounded to floats: a few float steps of a coordinate under 64 yalms (3.8e-6 each), under 2e-5 in all. 1e-4 keeps margin and is 500 times finer than the position step floor.</summary>
    private const float PointMiss = 1e-4f;

    /// <summary>How far the facing or up may be from a recorded point's at its arrive time, in radians. The timed rotation gives the point's own rotation there (full-freedom camera §2, smooth turns §1), rounded to floats (a few 6e-8 steps of a unit vector), and the frame rounds its facing through a look-at spot 10 yalms out: a float step of a coordinate under 128 yalms, 7.6e-6, over 10 yalms, 7.6e-7 rad. Under 1e-6 in all; 1e-5 keeps tenfold margin.</summary>
    private const float AimMiss = 1e-5f;

    /// <summary>How far the facing may be from the Look At point, in radians. Look At faces the point itself (full-freedom camera §4), so only rounding moves it: as for <see cref="AimMiss"/>, under 1e-6; 1e-5 keeps tenfold margin.</summary>
    private const float CentringLimit = 1e-5f;

    /// <summary>How far the horizon may lean where it's level, in radians. Outside vertical passages and Look At's turn spans the up is exactly upright or, over a loop, inverted (full-freedom camera §3 and §4, aim-flow §2), and both have a level right vector: only rounding leans it, the facing's (7.6e-7 rad, as for <see cref="AimMiss"/>) and a few 6e-8 steps in the cross product, normalising and arcsine. Under 1e-6 in all; 1e-5 keeps tenfold margin.</summary>
    private const float TiltLimit = 1e-5f;

    /// <summary>Seconds each side's speed is measured over, as <see cref="Run.Speed(double, double)"/>'s half-window: each side's chord spans 10 ms, 0.05 yalm at 5 yalms a second, from the point outward.</summary>
    private const double SpeedWindow = 5e-3;

    /// <summary>How far apart, in yalms a second, the speed just before and just after a point may be and still count as one speed. Rounding moves each end of a chord by up to about 1e-5 yalm (the float distance along the path, a 7.6e-6 step under 128 yalms, and the spline's coordinates, a few 3.8e-6 steps under 64), each side's speed by 2·1e-5 / 0.01 s = 2e-3 and the gap by 4e-3. A chord falls short of its arc by L²/24R² of its length L: on the tightest turn, the hairpin's, about 1.5 yalms round, 0.05² / (24·1.5²) = 4.6e-5, 2.3e-4 yalms a second. Under 5e-3 in all; 0.01, 0.2% of the default speed, keeps margin.</summary>
    private const float SpeedAgreement = 0.01f;

    /// <summary>How far either side of a point the world turn rate is sampled, in seconds, with a second sample twice as far to extrapolate from.</summary>
    private const double RateOffset = 2e-3;

    /// <summary>The central-difference half-window each turn rate is measured over, in seconds.</summary>
    private const double RateWindow = 1e-3;

    /// <summary>How far apart, in rad/s, the world turn rate just before and just after a point may be, each side extrapolated to the point from <see cref="RateOffset"/> and twice it, and still count as one rate. Extrapolating misses by the rate's second derivative times RateOffset², 4e-6 s². For recorded aim that's the timed rotation's cubic, at most 12φ/T³ + 6(|m₀| + |m₁|)/T² over a leg of T seconds turning φ between end rates m: the sweep's shortest legs, a yalm at 5 yalms a second, take 0.2 s and turn at most 72° (pan 40° with roll swinging 60°, 1.26 rad) between rates of at most φ/T = 6.3 rad/s, under 3800 rad/s³ and 0.015 rad/s a side. Look At's point and the look ahead's spot come no closer than 2 yalms, and the chord to them swings at most as fast as the camera's 5 yalms a second plus the spot's own 5, which moves too: 10 yalms a second over 2 yalms, 5 rad/s. A straight fly-by turning at ω at its closest has a rate whose second derivative peaks there at 2ω³, 250 rad/s³, 1e-3 rad/s a side. Rounding moves a position by about 1e-5 yalm (as for <see cref="SpeedAgreement"/>), a facing 2 yalms away by 5e-6 rad and a turn rate across a 2 ms window by 5e-3 rad/s, tripled by extrapolating (2a − b): 0.015 a side. At most 0.032 across both sides, recorded aim's 0.015 or the chord's 1e-3 a side with rounding; 0.04 rad/s (2.3°/s) keeps margin.</summary>
    private const float RateAgreement = 0.04f;

    /// <summary>The most a check's count or measure may be: <see cref="Check.WellFormed"/> and <see cref="Check.Snaps"/> count frames and snaps.</summary>
    private static float Limit(Check check) =>
        check switch
        {
            Check.WellFormed or Check.Snaps => 0f,
            Check.Whips => PictureSpinLimit,
            Check.PointsReached => PointMiss,
            Check.AimReached => AimMiss,
            Check.SpeedContinuous => SpeedAgreement,
            Check.TurnRateContinuous => RateAgreement,
            Check.HorizonLevel => TiltLimit,
            Check.LookAtCentred => CentringLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
        };

    /// <summary>Whether a check counts rather than bounds.</summary>
    private static bool Counts(Check check) => check is Check.WellFormed or Check.Snaps;

    /// <summary>What a spec says won't hold, by design.</summary>
    private static readonly Declared[] DeclaredExceptions =
    [
        new(
            Doubleback,
            LookAhead0,
            Check.Snaps,
            1f,
            "facing along the path, it turns round at once where the path runs straight back (3 s)",
            "smooth turns §2 (look ahead 0 is the exact tangent), as movement sweep spec:64's example; full-freedom camera §3 (line 48: a straight doubleback reverses a level facing); regression case \"Straight doubleback, look ahead 0: snaps round once\""
        ),
        new(
            Doubleback,
            LookAheadHalf,
            Check.Snaps,
            1f,
            "the spot ahead passes back through the camera and the aim turns round at once (2.75 s)",
            "full-freedom camera, Changes made while building, 2026-09-25 entries (line 126 names the straight doubleback's flip; line 127: any rule that faces a moving spot passing through the moving camera turns about 180° there); regression case \"Straight doubleback, look ahead 0.5: snaps round once\""
        ),
        new(
            Doubleback,
            LookAhead2,
            Check.Snaps,
            1f,
            "the spot ahead passes back through the camera and the aim turns round at once (2 s)",
            "full-freedom camera, Changes made while building, 2026-09-25 entries (line 126 names the straight doubleback's flip; line 127: any rule that faces a moving spot passing through the moving camera turns about 180° there)"
        ),
    ];

    /// <summary>Speed steps at a point found by the sweep, on every aim since speed is the path's.</summary>
    private static readonly Pending[] PendingSpeed =
    [
        new(Straight, null, Check.SpeedContinuous, 1, 0.056458f, SpeedStep("2", 4.944f, 5f)),
        new(Straight, null, Check.SpeedContinuous, 2, 0.056458f, SpeedStep("4", 5f, 4.943f)),
        new(GentleCurve, null, Check.SpeedContinuous, 1, 0.045676f, SpeedStep("1.565", 4.951f, 4.996f)),
        new(GentleCurve, null, Check.SpeedContinuous, 3, 0.045897f, SpeedStep("4.705", 4.996f, 4.95f)),
        new(SCurve, null, Check.SpeedContinuous, 1, 0.046873f, SpeedStep("1.544", 4.953f, 5f)),
        new(SCurve, null, Check.SpeedContinuous, 3, 0.047042f, SpeedStep("4.632", 5f, 4.953f)),
        new(Hairpin, null, Check.SpeedContinuous, 1, 0.13664f, SpeedStep("3.028", 4.863f, 5f)),
        new(Hairpin, null, Check.SpeedContinuous, 2, 0.13664f, SpeedStep("3.679", 5f, 4.863f)),
        new(Doubleback, null, Check.SpeedContinuous, 1, 0.16694f, SpeedStep("3", 4.979f, 4.812f)),
        new(Loop, null, Check.SpeedContinuous, 1, 0.068068f, SpeedStep("2.803", 4.924f, 4.992f)),
        new(Loop, null, Check.SpeedContinuous, 3, 0.030149f, SpeedStep("6.498", 4.986f, 4.956f)),
        new(Loop, null, Check.SpeedContinuous, 5, 0.030728f, SpeedStep("10.347", 4.956f, 4.986f)),
        new(Loop, null, Check.SpeedContinuous, 7, 0.068163f, SpeedStep("14.042", 4.991f, 4.923f)),
        new(Crane, null, Check.SpeedContinuous, 1, 0.049431f, SpeedStep("2.04", 4.948f, 4.998f)),
        new(Crane, null, Check.SpeedContinuous, 3, 0.049113f, SpeedStep("6.126", 4.998f, 4.949f)),
        new(Orbit, null, Check.SpeedContinuous, 1, 0.037662f, SpeedStep("1.853", 4.947f, 4.984f)),
        new(Orbit, null, Check.SpeedContinuous, 7, 0.036651f, SpeedStep("13.113", 4.984f, 4.947f)),
        new(Uneven, null, Check.SpeedContinuous, 1, 0.24588f, SpeedStep("0.2", 5.001f, 4.755f)),
        new(Uneven, null, Check.SpeedContinuous, 2, 0.24929f, SpeedStep("4.2", 4.755f, 5.005f)),
        new(Uneven, null, Check.SpeedContinuous, 3, 0.2943f, SpeedStep("4.4", 5.005f, 4.71f)),
    ];

    /// <summary>Turn-rate jumps at a point found by the sweep.</summary>
    private static readonly Pending[] PendingTurnRate =
    [
        new(GentleCurve, OverAPoint, Check.TurnRateContinuous, 1, 0.15702f, SpanEdge("1.565", 28.2f, 0.324f, 0.32f)),
        new(GentleCurve, OverAPoint, Check.TurnRateContinuous, 3, 0.15708f, SpanEdge("4.705", 28.2f, 0.32f, 0.324f)),
        new(GentleCurve, UnderAPoint, Check.TurnRateContinuous, 1, 0.15702f, SpanEdge("1.565", 28.2f, 0.324f, 0.32f)),
        new(GentleCurve, UnderAPoint, Check.TurnRateContinuous, 3, 0.15708f, SpanEdge("4.705", 28.2f, 0.32f, 0.324f)),
        new(SCurve, Above, Check.TurnRateContinuous, 1, 0.31151f, SpanEdge("1.544", 50.6f, 0.378f, 0.349f)),
        new(SCurve, Above, Check.TurnRateContinuous, 3, 0.31138f, SpanEdge("4.632", 50.6f, 0.348f, 0.377f)),
        new(SCurve, Below, Check.TurnRateContinuous, 1, 0.31151f, SpanEdge("1.544", 50.6f, 0.378f, 0.349f)),
        new(SCurve, Below, Check.TurnRateContinuous, 3, 0.31138f, SpanEdge("4.632", 50.6f, 0.348f, 0.377f)),
        new(SCurve, OverAPoint, Check.TurnRateContinuous, 1, 0.31151f, SpanEdge("1.544", 50.6f, 0.378f, 0.349f)),
        new(SCurve, OverAPoint, Check.TurnRateContinuous, 3, 0.31138f, SpanEdge("4.632", 50.6f, 0.348f, 0.377f)),
        new(SCurve, UnderAPoint, Check.TurnRateContinuous, 1, 0.31151f, SpanEdge("1.544", 50.6f, 0.378f, 0.349f)),
        new(SCurve, UnderAPoint, Check.TurnRateContinuous, 3, 0.31138f, SpanEdge("4.632", 50.6f, 0.348f, 0.377f)),
        new(Hairpin, LookAheadHalf, Check.TurnRateContinuous, 1, 0.053182f, WithSpeed("3.028", 2.156f, 2.209f)),
        new(Hairpin, LookAheadHalf, Check.TurnRateContinuous, 2, 0.046849f, WithSpeed("3.679", 1.384f, 1.338f)),
        new(Hairpin, Above, Check.TurnRateContinuous, 1, 0.67216f, SpanEdge("3.028", 83.2f, 0.43f, 0.57f)),
        new(Hairpin, Above, Check.TurnRateContinuous, 2, 0.67192f, SpanEdge("3.679", 83.1f, 0.57f, 0.43f)),
        new(Hairpin, Below, Check.TurnRateContinuous, 1, 0.67216f, SpanEdge("3.028", 83.2f, 0.43f, 0.57f)),
        new(Hairpin, Below, Check.TurnRateContinuous, 2, 0.67192f, SpanEdge("3.679", 83.1f, 0.57f, 0.43f)),
        new(Hairpin, OverAPoint, Check.TurnRateContinuous, 1, 1.1766f, SpanEdge("3.028", 79.7f, 1.164f, 0.478f)),
        new(Hairpin, UnderAPoint, Check.TurnRateContinuous, 1, 1.1766f, SpanEdge("3.028", 79.7f, 1.164f, 0.478f)),
        new(Doubleback, Above, Check.TurnRateContinuous, 1, 0.86573f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, Below, Check.TurnRateContinuous, 1, 0.86573f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, Beside, Check.TurnRateContinuous, 1, 0.86552f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, OverAPoint, Check.TurnRateContinuous, 1, 1.3524f, TurnsBack(0.811f, 0.541f)),
        new(Doubleback, UnderAPoint, Check.TurnRateContinuous, 1, 1.3524f, TurnsBack(0.811f, 0.541f)),
        new(Crane, Above, Check.TurnRateContinuous, 1, 0.37404f, SpanEdge("2.04", 70.0f, 0.205f, 0.391f)),
        new(Crane, Below, Check.TurnRateContinuous, 3, 0.37394f, SpanEdge("6.126", 70.1f, 0.39f, 0.205f)),
        new(Spiral, OverAPoint, Check.TurnRateContinuous, 3, 0.34834f, SpanEdge("9.016", 86.5f, 0.244f, 0.264f)),
        new(Spiral, UnderAPoint, Check.TurnRateContinuous, 5, 0.34839f, SpanEdge("15.085", 86.5f, 0.264f, 0.244f)),
        new(Orbit, OverAPoint, Check.TurnRateContinuous, 3, 0.2813f, SpanEdge("5.606", 52.3f, 0.325f, 0.313f)),
        new(Orbit, OverAPoint, Check.TurnRateContinuous, 5, 0.28161f, SpanEdge("9.36", 52.3f, 0.314f, 0.325f)),
        new(Orbit, UnderAPoint, Check.TurnRateContinuous, 3, 0.2813f, SpanEdge("5.606", 52.3f, 0.325f, 0.313f)),
        new(Orbit, UnderAPoint, Check.TurnRateContinuous, 5, 0.28161f, SpanEdge("9.36", 52.3f, 0.314f, 0.325f)),
        new(Uneven, Above, Check.TurnRateContinuous, 2, 0.11917f, WithSpeed("4.2", 1.136f, 1.255f)),
        new(Uneven, Above, Check.TurnRateContinuous, 3, 0.13286f, WithSpeed("4.4", 1.218f, 1.085f)),
        new(Uneven, Below, Check.TurnRateContinuous, 2, 0.11917f, WithSpeed("4.2", 1.136f, 1.255f)),
        new(Uneven, Below, Check.TurnRateContinuous, 3, 0.13286f, WithSpeed("4.4", 1.218f, 1.085f)),
        new(Uneven, Beside, Check.TurnRateContinuous, 2, 0.048447f, WithSpeed("4.2", 0.461f, 0.51f)),
        new(Uneven, Beside, Check.TurnRateContinuous, 3, 0.054605f, WithSpeed("4.4", 0.504f, 0.45f)),
        new(Uneven, OverAPoint, Check.TurnRateContinuous, 2, 0.11917f, WithSpeed("4.2", 1.136f, 1.255f)),
        new(Uneven, OverAPoint, Check.TurnRateContinuous, 3, 0.13286f, WithSpeed("4.4", 1.218f, 1.085f)),
        new(Uneven, UnderAPoint, Check.TurnRateContinuous, 2, 0.11917f, WithSpeed("4.2", 1.136f, 1.255f)),
        new(Uneven, UnderAPoint, Check.TurnRateContinuous, 3, 0.13286f, WithSpeed("4.4", 1.218f, 1.085f)),
    ];

    /// <summary>What's seen where the speed steps at a point <paramref name="time"/> seconds in.</summary>
    private static string SpeedStep(string time, float before, float after) =>
        string.Create(CultureInfo.InvariantCulture, $"at {time} s: {before} yalms/s just before, {after} just after");

    /// <summary>What's seen where Look At's turn rate swings its axis at a point, as it does where the up rejoins upright at a turn span's end.</summary>
    private static string SpanEdge(string time, float axes, float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"at {time} s the turn rate's axis swings {axes}°, {before} rad/s before and {after} after; the camera passes under or over the Look At point next to it, so a turn span (aim-flow §2) likely starts or ends there, as on the crane, where the up is seen meeting upright at an angle at point 1"
        );

    /// <summary>What's seen where Look At's turn rate reverses at the doubleback's turn-back point.</summary>
    private static string TurnsBack(float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the turn rate reverses at the turn-back point (3 s), {before} rad/s before and {after} after, axes 180° apart: the camera reverses at full speed there, which constant speed along a path that runs straight back seems to imply, but no spec says so for Look At"
        );

    /// <summary>What's seen where the turn rate keeps its axis but changes size at a point, with the speed step there.</summary>
    private static string WithSpeed(string time, float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"at {time} s the turn rate keeps its axis but goes from {before} to {after} rad/s, where the speed steps too"
        );

    /// <summary>Failures waiting for the user's triage.</summary>
    private static readonly Pending[] PendingTriage = [.. PendingSpeed, .. PendingTurnRate];

    /// <summary>Every check that applies to <paramref name="combination"/>, measured on <paramref name="run"/>.</summary>
    private static List<Measure> MeasureAll(Combination combination, Track track, Run run)
    {
        var frames = Every(FrameSeconds, 0.0, run.Duration).ToArray();
        var positions = combination.Shape.Positions;
        var through = Enumerable
            .Range(1, positions.Length - 2)
            .Where(p => TrackEditing.HoldSeconds(track, p) == 0f)
            .ToArray();
        var broken = frames.Where(t => WellFormed.FirstBroken(run.At(t)) is not null).ToArray();
        var snaps = run.Snaps();
        var measures = new List<Measure>
        {
            new(Check.WellFormed, broken.Length, broken.Length > 0 ? $"first at {broken[0]:0.###} s" : ""),
            new(Check.Snaps, snaps.Count, string.Join("; ", snaps)),
            Worst(
                Check.PointsReached,
                run,
                Enumerable.Range(0, positions.Length),
                p => (Vector3.Distance(run.At(run.Arrive(p)).Position, positions[p]), "")
            ),
            Worst(Check.SpeedContinuous, run, through, p => SpeedGap(run, run.Arrive(p))),
        };

        if (track.Aim == AimMode.AimKeys)
        {
            measures.Add(
                Worst(
                    Check.AimReached,
                    run,
                    Enumerable.Range(0, positions.Length),
                    p => (AimOff(run.At(run.Arrive(p)), track.Points[p]), "")
                )
            );
        }
        else
        {
            measures.Add(AtPeak(Check.Whips, run.LargestTwistAt()));
            // Loops' inverted stretches are checked too, beyond what the spec asks: an inverted picture's right vector stays level.
            measures.Add(AtPeak(Check.HorizonLevel, LargestAt(LevelFrames(run, track, frames), run.HorizonTilt)));
        }

        // Direction of travel with no look ahead faces along the path, whose curvature changes at each point by construction.
        if (track.Aim != AimMode.PathTangent || track.LookAhead > 0f)
            measures.Add(Worst(Check.TurnRateContinuous, run, through, p => RateGap(run, run.Arrive(p))));
        if (track.Aim == AimMode.LookAt)
            measures.Add(AtPeak(Check.LookAtCentred, LargestAt(frames, t => run.Centring(t, track.LookAt))));
        return measures;
    }

    /// <summary>The <paramref name="frames"/> where the horizon is level: outside vertical passages for Direction of travel, and outside every stretch a turn span may take for Look At (<see cref="SpanStretches"/>). LevelUp finds each passage's and span's edge to within <see cref="LevelUp.EdgeSeconds"/> of where the facing crosses it, so a frame near an edge counts only when the facing is outside that far either side too.</summary>
    private static IEnumerable<double> LevelFrames(Run run, Track track, double[] frames)
    {
        if (track.Aim != AimMode.LookAt)
            return frames.Where(t => Beyond(run, t, LevelUp.PassageSideways));
        var span = SpanStretches(run, track.Points.Count, frames);
        return frames.Where((_, i) => !span[i]);
    }

    /// <summary>Which <paramref name="frames"/> a Look At turn span may cover (aim-flow §2): around each vertical passage, from leaving the last point at or before it to reaching the first point at or after it, and never beyond 60° from straight up or down; a passage the shot starts inside or never leaves has no span (LevelUp's Reaches), so only its own frames are left out. Passages are found at the frames: one that falls between frames leaves its span checked, which only makes the check stricter and fails loudly.</summary>
    private static bool[] SpanStretches(Run run, int points, double[] frames)
    {
        var slop = (double)LevelUp.EdgeSeconds;
        bool InPassage(int i) => !Outside(run, frames[i], LevelUp.PassageSideways);
        bool InReach(int i) => !Beyond(run, frames[i], LevelUp.SpanSideways);
        var span = new bool[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            if (!InPassage(i))
                continue;
            var j = i;
            while (j + 1 < frames.Length && InPassage(j + 1))
                j++;

            if (i == 0 || j == frames.Length - 1)
            {
                for (var k = Math.Max(0, i - 1); k <= Math.Min(frames.Length - 1, j + 1); k++)
                    span[k] |= !Beyond(run, frames[k], LevelUp.PassageSideways);
                i = j;
                continue;
            }

            // The passage starts after the frame before this run of passage frames and ends before the frame after it.
            var (enter, leave) = (frames[i - 1], frames[j + 1]);
            var from = Enumerable.Range(0, points).Select(run.Depart).Where(t => t <= enter).DefaultIfEmpty(0.0).Max();
            var to = Enumerable
                .Range(0, points)
                .Select(run.Arrive)
                .Where(t => t >= leave)
                .DefaultIfEmpty(run.Duration)
                .Min();
            var (a, b) = (i, j);
            while (a > 0 && InReach(a - 1))
                a--;
            while (b + 1 < frames.Length && InReach(b + 1))
                b++;
            for (var k = a; k <= b; k++)
                span[k] |= frames[k] >= from - slop && frames[k] <= to + slop;
            i = j;
        }

        return span;
    }

    /// <summary>Whether the facing has a sideways part of at least <paramref name="reach"/> at <paramref name="time"/> and <see cref="LevelUp.EdgeSeconds"/> either side.</summary>
    private static bool Beyond(Run run, double time, float reach) =>
        new[] { -LevelUp.EdgeSeconds, 0.0, LevelUp.EdgeSeconds }.All(d => Outside(run, time + d, reach));

    /// <summary>Whether the facing at <paramref name="time"/>, kept within the run, has a sideways part of at least <paramref name="reach"/>.</summary>
    private static bool Outside(Run run, double time, float reach) =>
        CameraRotation.Sideways(run.At(Math.Clamp(time, 0.0, run.Duration)).Forward) >= reach;

    /// <summary>A measure from its peak over frames, 0 where no frame was measured.</summary>
    private static Measure AtPeak(Check check, Peak peak) =>
        double.IsNaN(peak.Time) && !float.IsNaN(peak.Value)
            ? new Measure(check, 0f, "")
            : new Measure(check, peak.Value, $"at {peak.Time:0.###} s");

    /// <summary>The worst of <paramref name="measure"/> over <paramref name="points"/>, 0 with none.</summary>
    private static Measure Worst(
        Check check,
        Run run,
        IEnumerable<int> points,
        Func<int, (float Value, string Detail)> measure
    )
    {
        var each = points
            .Select(p =>
            {
                var (value, detail) = measure(p);
                return new PointMeasure(p, value, $"at point {p} ({run.Arrive(p):0.###} s){detail}");
            })
            .ToList();
        var worst = each.MaxBy(m => float.IsNaN(m.Value) ? float.PositiveInfinity : m.Value);
        return worst is null ? new Measure(check, 0f, "", each) : new Measure(check, worst.Value, worst.Where, each);
    }

    /// <summary>How far apart the speed is just before and just after <paramref name="time"/>, in yalms a second, with each.</summary>
    private static (float Gap, string Detail) SpeedGap(Run run, double time)
    {
        var (before, after) = (run.Speed(time - SpeedWindow, SpeedWindow), run.Speed(time + SpeedWindow, SpeedWindow));
        return (MathF.Abs(after - before), $": {before:0.###} before, {after:0.###} after yalms/s");
    }

    /// <summary>How far apart the world turn rate is just before and just after <paramref name="time"/>, each extrapolated to it, in rad/s, with each one's size.</summary>
    private static (float Gap, string Detail) RateGap(Run run, double time)
    {
        Vector3 Side(double sign) =>
            (2f * run.WorldTurnRate(time + (sign * RateOffset), RateWindow))
            - run.WorldTurnRate(time + (sign * 2.0 * RateOffset), RateWindow);
        var (before, after) = (Side(-1.0), Side(1.0));
        return (
            (after - before).Length(),
            $": {before.Length():0.###} before, {after.Length():0.###} after rad/s, axes {Vectors.AngleBetween(before, after) / Deg:0.#}° apart"
        );
    }

    /// <summary>How far <paramref name="frame"/>'s facing or up is from <paramref name="point"/>'s recorded ones, in radians.</summary>
    private static float AimOff(CameraState frame, ControlPoint point)
    {
        var rotation = CameraRotation.FromAngles(point.Yaw, point.Pitch, point.Roll);
        return MathF.Max(
            Vectors.AngleBetween(frame.Forward, CameraRotation.Forward(rotation)),
            Vectors.AngleBetween(frame.Up, CameraRotation.Up(rotation))
        );
    }

    /// <summary>How a check came out on a combination.</summary>
    private enum Status
    {
        Pass,
        Declared,
        Pending,
        Fail,
    }

    /// <summary>A check's measure on a combination, how it came out, and why.</summary>
    private sealed record Verdict(Measure Measure, Status Status, string Why);

    /// <summary>One combination's verdicts, and the largest turn between 60 fps frames in degrees a second, for the report.</summary>
    private sealed record Outcome(Combination Combination, List<Verdict> Verdicts, float TurnRate)
    {
        internal Status Status => Verdicts.Max(v => v.Status);

        internal float? Value(Check check) => Verdicts.FirstOrDefault(v => v.Measure.Check == check)?.Measure.Value;
    }

    private static bool Matches(Shape shape, Aim? aim, Check check, Combination combination, Check measured) =>
        shape == combination.Shape && (aim is null || aim == combination.Aim) && check == measured;

    /// <summary>How <paramref name="measure"/> comes out on <paramref name="combination"/> against its limit and the lists.</summary>
    private static Verdict Judge(Combination combination, Measure measure)
    {
        var (check, value) = (measure.Check, measure.Value);
        var limit = Limit(check);
        if (
            DeclaredExceptions.FirstOrDefault(d => Matches(d.Shape, d.Aim, d.Check, combination, check)) is { } declared
        )
        {
            var holds = Counts(check) ? value == declared.Expected : value <= declared.Expected && !(value <= limit);
            return holds
                ? new Verdict(measure, Status.Declared, $"{check}: {declared.Reason} ({declared.Spec})")
                : new Verdict(
                    measure,
                    Status.Fail,
                    $"{check} {value:G5}, declared {declared.Expected:G5} ({declared.Reason}) {measure.Where}"
                );
        }

        var pending = PendingTriage.Where(p => Matches(p.Shape, p.Aim, p.Check, combination, check)).ToArray();
        if (pending.Length > 0)
            return JudgePending(measure, pending);

        if (value <= limit)
            return new Verdict(measure, Status.Pass, "");
        var over = (measure.Points ?? [])
            .Where(m => !(m.Value <= limit))
            .Select(m => $"{m.Value:G5} {m.Where}")
            .ToList();
        return new Verdict(
            measure,
            Status.Fail,
            over.Count == 0
                ? $"{check} {value:G5} over {limit:G5} {measure.Where}"
                : $"{check} over {limit:G5}: {string.Join("; ", over)}"
        );
    }

    /// <summary>How <paramref name="measure"/> comes out against its <paramref name="pending"/> entries: each still where it was listed and still over the limit, and every other point within the limit.</summary>
    private static Verdict JudgePending(Measure measure, Pending[] pending)
    {
        var check = measure.Check;
        var limit = Limit(check);
        var points = measure.Points ?? [];
        var problems = new List<string>();
        foreach (var entry in pending)
        {
            var at = entry.Point is { } p ? points.FirstOrDefault(m => m.Point == p) : null;
            var (value, where) = at is null ? (measure.Value, measure.Where) : (at.Value, at.Where);
            if (entry.Point is null && measure.Points is not null)
                problems.Add($"{check} is measured at each point, so its pending entry needs one ({entry.Seen})");
            else if (entry.Point is not null && at is null)
                problems.Add($"{check} has no point {entry.Point}, pending ({entry.Seen})");
            else if (value <= limit)
                problems.Add($"{check} {value:G5} {where} is within {limit:G5}: drop the pending entry ({entry.Seen})");
            else if (!(Counts(check) ? value == entry.Measured : MathF.Abs(value - entry.Measured) <= limit))
                problems.Add($"{check} {value:G5}, pending at {entry.Measured:G5} ({entry.Seen}) {where}");
        }

        var listed = pending.Select(e => e.Point).OfType<int>().ToHashSet();
        problems.AddRange(
            points
                .Where(m => !listed.Contains(m.Point) && !(m.Value <= limit))
                .Select(m => $"{check} {m.Value:G5} over {limit:G5} {m.Where}")
        );

        return problems.Count == 0
            ? new Verdict(measure, Status.Pending, $"{check}: {string.Join("; ", pending.Select(e => e.Seen))}")
            : new Verdict(measure, Status.Fail, string.Join("; ", problems));
    }

    /// <summary>Plays and judges <paramref name="combination"/>.</summary>
    private static Outcome Play(Combination combination)
    {
        var track = combination.Track();
        var run = new Run(track);
        var verdicts = MeasureAll(combination, track, run).Select(m => Judge(combination, m)).ToList();
        var turn = DistanceDegrees(run.LargestTurn(FrameSeconds)) / (float)FrameSeconds;
        return new Outcome(combination, verdicts, turn);
    }

    [Fact]
    public void EveryShapeUnderEveryAimPassesEveryCheckOrIsListed()
    {
        var outcomes = Combinations().AsParallel().AsOrdered().Select(Play).ToList();
        if (Environment.GetEnvironmentVariable(ReportVariable) == "1")
            WriteReport(outcomes);

        var problems = outcomes
            .SelectMany(o =>
                o.Verdicts.Where(v => v.Status == Status.Fail).Select(v => $"{o.Combination.Name}: {v.Why}")
            )
            .ToList();
        var measured = outcomes.SelectMany(o => o.Verdicts.Select(v => (o.Combination, v.Measure.Check))).ToList();
        problems.AddRange(
            DeclaredExceptions
                .Where(d => !measured.Any(m => Matches(d.Shape, d.Aim, d.Check, m.Combination, m.Check)))
                .Select(d => $"Declared {d.Shape.Name} / {d.Aim?.Name ?? "every aim"} / {d.Check} matches no measure")
        );
        problems.AddRange(
            PendingTriage
                .Where(p => !measured.Any(m => Matches(p.Shape, p.Aim, p.Check, m.Combination, m.Check)))
                .Select(p => $"Pending {p.Shape.Name} / {p.Aim?.Name ?? "every aim"} / {p.Check} matches no measure")
        );
        Assert.True(problems.Count == 0, $"{problems.Count} undeclared:\n{string.Join("\n", problems)}");
    }

    /// <summary>Writes one row per combination to a dated file under the test project's obj folder.</summary>
    private static void WriteReport(List<Outcome> outcomes)
    {
        string Figure(float? value, float scale, string format) =>
            value is { } v ? (v * scale).ToString(format, CultureInfo.InvariantCulture) : "–";

        string Tally(IEnumerable<Status> statuses) =>
            string.Join(
                ", ",
                Enum.GetValues<Status>().Select(s => $"{statuses.Count(x => x == s)} {s.ToString().ToLowerInvariant()}")
            );

        var checks = outcomes.SelectMany(o => o.Verdicts).Select(v => v.Status).ToList();
        var text = new StringBuilder();
        text.AppendLine("# Movement sweep")
            .AppendLine()
            .AppendLine(
                CultureInfo.InvariantCulture,
                $"{outcomes.Count} combinations, each counted by its worst check: {Tally(outcomes.Select(o => o.Status))}. {checks.Count} checks: {Tally(checks)}."
            )
            .AppendLine()
            .AppendLine(
                "| Combination | Largest turn rate (°/s) | Largest speed change through a point (yalms/s) | Largest horizon tilt (°) | Largest centring error (°) | Snaps | Result | Reason |"
            )
            .AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var outcome in outcomes)
        {
            var reasons = outcome.Verdicts.Where(v => v.Status != Status.Pass).Select(v => $"{v.Status}: {v.Why}");
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {outcome.Combination.Name} | {Figure(outcome.TurnRate, 1f, "0.#")} | {Figure(outcome.Value(Check.SpeedContinuous), 1f, "0.####")} | {Figure(outcome.Value(Check.HorizonLevel), 1f / Deg, "0.#####")} | {Figure(outcome.Value(Check.LookAtCentred), 1f / Deg, "0.#####")} | {Figure(outcome.Value(Check.Snaps), 1f, "0")} | {outcome.Status.ToString().ToLowerInvariant()} | {string.Join("<br>", reasons).Replace("|", "\\|", StringComparison.Ordinal)} |"
            );
        }

        var folder = Path.Combine(RepositoryRoot(), "tests", "Vista.Tests", "obj", "sweep-reports");
        Directory.CreateDirectory(folder);
        var name = $"sweep-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md";
        File.WriteAllText(Path.Combine(folder, name), text.ToString());
    }
}
