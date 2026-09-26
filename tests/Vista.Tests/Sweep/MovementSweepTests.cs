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
    private sealed record Measure(Check Check, float Value, string Where);

    /// <summary>A failure a spec implies: the shape, the aim setting (every aim when null), the check, the count it has or the bound it stays within, why, and the spec it follows from.</summary>
    private sealed record Declared(Shape Shape, Aim? Aim, Check Check, float Expected, string Reason, string Spec);

    /// <summary>A failure waiting for the user's triage: the shape, the aim setting (every aim when null), the check, the value measured when it was listed, and what's seen. It fails if the measure moves further than the check's own limit from that value.</summary>
    private sealed record Pending(Shape Shape, Aim? Aim, Check Check, float Measured, string Seen);

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

    /// <summary>Point <paramref name="point"/>'s look in degrees: pitch 45°, then 90°, then yaw turned 180° at 45°, each cycle of three carrying on from the last one's yaw so the camera keeps turning over the same way.</summary>
    private static (float Yaw, float Pitch, float Roll) OverTheTopLook(int point) =>
        ((180f * (point / 3)) + (point % 3 == 2 ? 180f : 0f), point % 3 == 1 ? 90f : 45f, 0f);

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

    /// <summary>How near a passage's or turn span's edge a frame may be and still count as level: LevelUp finds each edge to within a millisecond of where the facing crosses it, so a frame counts only when the facing is outside a millisecond either side too.</summary>
    private const double EdgeSlop = Millisecond;

    /// <summary>A Look At turn span reaches no further than 60° from straight up or down (aim-flow §2), as a facing's sideways part.</summary>
    private static readonly float SpanReach = MathF.Sin(60f * Deg);

    /// <summary>Seconds each side's speed is measured over, as <see cref="Run.Speed(double, double)"/>'s half-window: each side's chord spans 10 ms, 0.05 yalm at 5 yalms a second, from the point outward.</summary>
    private const double SpeedWindow = 5e-3;

    /// <summary>How far apart, in yalms a second, the speed just before and just after a point may be and still count as one speed. Rounding moves each end of a chord by up to about 1e-5 yalm (the float distance along the path, a 7.6e-6 step under 128 yalms, and the spline's coordinates, a few 3.8e-6 steps under 64), each side's speed by 2·1e-5 / 0.01 s = 2e-3 and the gap by 4e-3. A chord falls short of its arc by L²/24R² of its length L: on the tightest turn, the hairpin's, about 1.5 yalms round, 0.05² / (24·1.5²) = 4.6e-5, 2.3e-4 yalms a second. Under 5e-3 in all; 0.01, 0.2% of the default speed, keeps margin.</summary>
    private const float SpeedAgreement = 0.01f;

    /// <summary>How far either side of a point the world turn rate is sampled, in seconds, with a second sample twice as far to extrapolate from.</summary>
    private const double RateOffset = 2e-3;

    /// <summary>The central-difference half-window each turn rate is measured over, in seconds.</summary>
    private const double RateWindow = 1e-3;

    /// <summary>How far apart, in rad/s, the world turn rate just before and just after a point may be, each side extrapolated to the point from <see cref="RateOffset"/> and twice it, and still count as one rate. Extrapolating misses by the rate's second derivative times RateOffset², 4e-6 s². For recorded aim that's the timed rotation's cubic, at most 12φ/T³ + 6(|m₀| + |m₁|)/T² over a leg of T seconds turning φ between end rates m: the sweep's shortest legs, a yalm at 5 yalms a second, take 0.2 s and turn at most 72° (pan 40° with roll swinging 60°, 1.26 rad) between rates of at most φ/T = 6.3 rad/s, under 3800 rad/s³ and 0.015 rad/s a side. Look At and the look ahead turn no faster than 5 yalms a second over the 2 yalms they come closest, 2.5 rad/s, whose second derivative is about 2.5³ = 16 rad/s³, 6e-5 rad/s a side. Rounding moves a position by about 1e-5 yalm (as for <see cref="SpeedAgreement"/>), a facing 2 yalms away by 5e-6 rad and a turn rate across a 2 ms window by 5e-3 rad/s, tripled by extrapolating (2a − b): 0.015 a side. Under 0.03 across both sides; 0.04 rad/s (2.3°/s) keeps margin.</summary>
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
            "full-freedom camera §3; regression case \"Straight doubleback, look ahead 0: snaps round once\""
        ),
        new(
            Doubleback,
            LookAheadHalf,
            Check.Snaps,
            1f,
            "the spot ahead passes back through the camera and the aim turns round at once (2.75 s)",
            "full-freedom camera §6, changes of 2026-09-25; regression case \"Straight doubleback, look ahead 0.5: snaps round once\""
        ),
        new(
            Doubleback,
            LookAhead2,
            Check.Snaps,
            1f,
            "the spot ahead passes back through the camera and the aim turns round at once (2 s)",
            "full-freedom camera §6, changes of 2026-09-25: a camera passing through the moving spot flips by the look ahead's definition"
        ),
    ];

    /// <summary>Speed steps at a point found by the first sweep, on every aim since it's the path's.</summary>
    private static readonly Pending[] PendingSpeed =
    [
        new(
            Straight,
            null,
            Check.SpeedContinuous,
            0.056458f,
            "slows to 4.944 yalms/s just before point 1 (2 s) and is back to 5 just after"
        ),
        new(
            GentleCurve,
            null,
            Check.SpeedContinuous,
            0.045897f,
            "4.996 yalms/s just before point 3 (4.705 s), 4.950 just after"
        ),
        new(
            SCurve,
            null,
            Check.SpeedContinuous,
            0.047042f,
            "5.000 yalms/s just before point 3 (4.632 s), 4.953 just after"
        ),
        new(
            Hairpin,
            null,
            Check.SpeedContinuous,
            0.13664f,
            "4.863 yalms/s just before point 1 (3.028 s), 5.000 just after"
        ),
        new(
            Doubleback,
            null,
            Check.SpeedContinuous,
            0.16694f,
            "4.979 yalms/s into the turn-back point 1 (3 s), 4.812 out of it"
        ),
        new(
            Loop,
            null,
            Check.SpeedContinuous,
            0.068163f,
            "4.991 yalms/s just before point 7 (14.042 s), 4.923 just after"
        ),
        new(
            Crane,
            null,
            Check.SpeedContinuous,
            0.049431f,
            "4.948 yalms/s just before point 1 (2.04 s), 4.998 just after"
        ),
        new(
            Orbit,
            null,
            Check.SpeedContinuous,
            0.037662f,
            "4.947 yalms/s just before point 1 (1.853 s), 4.984 just after"
        ),
        new(
            Uneven,
            null,
            Check.SpeedContinuous,
            0.2943f,
            "5.005 yalms/s at the end of the 1-yalm leg into point 3 (4.4 s), 4.710 at the start of the 20-yalm leg out"
        ),
    ];

    /// <summary>Turn-rate jumps at a point found by the first sweep.</summary>
    private static readonly Pending[] PendingTurnRate =
    [
        new(GentleCurve, OverAPoint, Check.TurnRateContinuous, 0.15708f, SpanEdge("3 (4.705 s)", 28.2f, 0.32f, 0.324f)),
        new(
            GentleCurve,
            UnderAPoint,
            Check.TurnRateContinuous,
            0.15708f,
            SpanEdge("3 (4.705 s)", 28.2f, 0.32f, 0.324f)
        ),
        new(SCurve, Above, Check.TurnRateContinuous, 0.31151f, SpanEdge("1 (1.544 s)", 50.6f, 0.378f, 0.349f)),
        new(SCurve, Below, Check.TurnRateContinuous, 0.31151f, SpanEdge("1 (1.544 s)", 50.6f, 0.378f, 0.349f)),
        new(SCurve, OverAPoint, Check.TurnRateContinuous, 0.31151f, SpanEdge("1 (1.544 s)", 50.6f, 0.378f, 0.349f)),
        new(SCurve, UnderAPoint, Check.TurnRateContinuous, 0.31151f, SpanEdge("1 (1.544 s)", 50.6f, 0.378f, 0.349f)),
        new(Hairpin, Above, Check.TurnRateContinuous, 0.67216f, SpanEdge("1 (3.028 s)", 83.2f, 0.43f, 0.57f)),
        new(Hairpin, Below, Check.TurnRateContinuous, 0.67216f, SpanEdge("1 (3.028 s)", 83.2f, 0.43f, 0.57f)),
        new(Hairpin, OverAPoint, Check.TurnRateContinuous, 1.1766f, SpanEdge("1 (3.028 s)", 79.7f, 1.164f, 0.478f)),
        new(Hairpin, UnderAPoint, Check.TurnRateContinuous, 1.1766f, SpanEdge("1 (3.028 s)", 79.7f, 1.164f, 0.478f)),
        new(Crane, Above, Check.TurnRateContinuous, 0.37404f, SpanEdge("1 (2.04 s)", 70f, 0.205f, 0.391f)),
        new(Crane, Below, Check.TurnRateContinuous, 0.37394f, SpanEdge("3 (6.126 s)", 70.1f, 0.39f, 0.205f)),
        new(Spiral, OverAPoint, Check.TurnRateContinuous, 0.34834f, SpanEdge("3 (9.016 s)", 86.5f, 0.244f, 0.264f)),
        new(Spiral, UnderAPoint, Check.TurnRateContinuous, 0.34839f, SpanEdge("5 (15.085 s)", 86.5f, 0.264f, 0.244f)),
        new(Orbit, OverAPoint, Check.TurnRateContinuous, 0.28161f, SpanEdge("5 (9.36 s)", 52.3f, 0.314f, 0.325f)),
        new(Orbit, UnderAPoint, Check.TurnRateContinuous, 0.28161f, SpanEdge("5 (9.36 s)", 52.3f, 0.314f, 0.325f)),
        new(Doubleback, Above, Check.TurnRateContinuous, 0.86573f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, Below, Check.TurnRateContinuous, 0.86573f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, Beside, Check.TurnRateContinuous, 0.86552f, TurnsBack(0.519f, 0.346f)),
        new(Doubleback, OverAPoint, Check.TurnRateContinuous, 1.3524f, TurnsBack(0.811f, 0.541f)),
        new(Doubleback, UnderAPoint, Check.TurnRateContinuous, 1.3524f, TurnsBack(0.811f, 0.541f)),
        new(Uneven, Above, Check.TurnRateContinuous, 0.13286f, WithSpeed("3 (4.4 s)", 1.218f, 1.085f)),
        new(Uneven, Below, Check.TurnRateContinuous, 0.13286f, WithSpeed("3 (4.4 s)", 1.218f, 1.085f)),
        new(Uneven, OverAPoint, Check.TurnRateContinuous, 0.13286f, WithSpeed("3 (4.4 s)", 1.218f, 1.085f)),
        new(Uneven, UnderAPoint, Check.TurnRateContinuous, 0.13286f, WithSpeed("3 (4.4 s)", 1.218f, 1.085f)),
        new(Uneven, Beside, Check.TurnRateContinuous, 0.054605f, WithSpeed("3 (4.4 s)", 0.504f, 0.45f)),
        new(Hairpin, LookAheadHalf, Check.TurnRateContinuous, 0.053182f, WithSpeed("1 (3.028 s)", 2.156f, 2.209f)),
    ];

    /// <summary>What's seen where Look At's turn rate swings its axis at a point, as it does where the up rejoins upright at a turn span's end.</summary>
    private static string SpanEdge(string point, float axes, float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the turn rate's axis swings {axes}° at point {point}, {before} rad/s before and {after} after; the camera passes under or over the Look At point next to it, so a turn span (aim-flow §2) likely starts or ends there, as on the crane, where the up is seen meeting upright at an angle at point 1"
        );

    /// <summary>What's seen where Look At's turn rate reverses at the doubleback's turn-back point.</summary>
    private static string TurnsBack(float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the turn rate reverses at the turn-back point 1 (3 s), {before} rad/s before and {after} after, axes 180° apart: the camera reverses at full speed there, which constant speed along a path that runs straight back seems to imply, but no spec says so for Look At"
        );

    /// <summary>What's seen where the turn rate keeps its axis but changes size at a point, with the speed step there.</summary>
    private static string WithSpeed(string point, float before, float after) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the turn rate keeps its axis but goes from {before} to {after} rad/s at point {point}, where the speed steps too (pending above)"
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
            var reach = track.Aim == AimMode.LookAt ? SpanReach : LevelUp.PassageSideways;
            var level = frames.Where(t => new[] { -EdgeSlop, 0.0, EdgeSlop }.All(d => Outside(run, t + d, reach)));
            measures.Add(AtPeak(Check.HorizonLevel, LargestAt(level, run.HorizonTilt)));
        }

        // Direction of travel with no look ahead faces along the path, whose curvature changes at each point by construction.
        if (track.Aim != AimMode.PathTangent || track.LookAhead > 0f)
            measures.Add(Worst(Check.TurnRateContinuous, run, through, p => RateGap(run, run.Arrive(p))));
        if (track.Aim == AimMode.LookAt)
            measures.Add(AtPeak(Check.LookAtCentred, LargestAt(frames, t => run.Centring(t, track.LookAt))));
        return measures;
    }

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
        var worst = points
            .Select(p => (Point: p, Result: measure(p)))
            .DefaultIfEmpty((Point: -1, Result: (Value: 0f, Detail: "")))
            .MaxBy(w => float.IsNaN(w.Result.Value) ? float.PositiveInfinity : w.Result.Value);
        return worst.Point < 0
            ? new Measure(check, 0f, "")
            : new Measure(
                check,
                worst.Result.Value,
                $"at point {worst.Point} ({run.Arrive(worst.Point):0.###} s){worst.Result.Detail}"
            );
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

        if (PendingTriage.FirstOrDefault(p => Matches(p.Shape, p.Aim, p.Check, combination, check)) is { } pending)
        {
            var holds = Counts(check) ? value == pending.Measured : MathF.Abs(value - pending.Measured) <= limit;
            return holds
                ? new Verdict(measure, Status.Pending, $"{check}: {pending.Seen}")
                : new Verdict(
                    measure,
                    Status.Fail,
                    $"{check} {value:G5}, pending at {pending.Measured:G5} ({pending.Seen}) {measure.Where}"
                );
        }

        return value <= limit
            ? new Verdict(measure, Status.Pass, "")
            : new Verdict(measure, Status.Fail, $"{check} {value:G5} over {limit:G5} {measure.Where}");
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

        var text = new StringBuilder();
        text.AppendLine("# Movement sweep")
            .AppendLine()
            .AppendLine(
                CultureInfo.InvariantCulture,
                $"{outcomes.Count} combinations: {string.Join(", ", Enum.GetValues<Status>().Select(s => $"{outcomes.Count(o => o.Status == s)} {s.ToString().ToLowerInvariant()}"))}."
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
