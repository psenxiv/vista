using System.Globalization;
using System.Numerics;
using System.Text;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

namespace Vista.Tests.Sweep;

/// <summary>Every path shape played under every aim setting, checked frame by frame for faults a viewer would notice.</summary>
public class MovementSweepTests
{
    /// <summary>The environment variable that makes the sweep also write its report.</summary>
    private const string ReportVariable = "VISTA_SWEEP_REPORT";

    /// <summary>What the sweep checks on every combination.</summary>
    private enum Check
    {
        WellFormed,
        Snaps,
        Whips,
        SpeedJump,
        TurnCap,
        TurnJump,
    }

    /// <summary>A path shape: its name, its points' positions, the point held <see cref="HoldSeconds"/> if any, and an adjustment applied to the built track after the hold, if any.</summary>
    private sealed record Shape(string Name, Vector3[] Positions, int? Held = null, Func<Track, Track>? Adjust = null);

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
            track = Shape.Held is { } held ? TrackEditing.SetHold(track, held, HoldSeconds) : track;
            return Shape.Adjust is { } adjust ? adjust(track) : track;
        }
    }

    /// <summary>A check's worst value on a combination, and when it happened.</summary>
    private sealed record Measure(Check Check, float Value, string Where);

    /// <summary>A check a combination fails by design: the shape, the aim setting, the check, the count it has or the most it may reach, and why.</summary>
    private sealed record Declared(Shape Shape, Aim Aim, Check Check, float Expected, string Reason);

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
    private static readonly Shape EasedCorner = new(
        "Eased corner",
        PathShapes.Corner,
        Adjust: t =>
            Enumerable.Range(1, t.Points.Count - 1).Aggregate(t, (x, leg) => LegEasing.Set(x, leg, Easing.EaseInOut))
    );
    private static readonly Shape SlowDrift = new(
        "Slow drift",
        PathShapes.SlowDrift,
        Adjust: t => TrackEditing.SetLegDuration(t, 2, 4f)
    );
    private static readonly Shape OnTheSpot = new(
        "Pan on the spot",
        PathShapes.OnTheSpot,
        Adjust: t => TrackEditing.SetLegDuration(t, 1, 3f)
    );

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
    private static readonly Aim LookAheadNear = new("Direction of travel: look ahead 2.5", ps => Travel(ps, 2.5f));
    private static readonly Aim LookAheadFar = new("Direction of travel: look ahead 10", ps => Travel(ps, 10f));
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

    /// <summary>Every path shape the sweep plays.</summary>
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
        EasedCorner,
        SlowDrift,
        OnTheSpot,
    ];

    /// <summary>Every aim setting each shape is played under.</summary>
    private static readonly Aim[] Aims =
    [
        Pan,
        Tilt,
        OverTheTop,
        WithRoll,
        LookAhead0,
        LookAheadNear,
        LookAheadFar,
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

    /// <summary>Direction of travel through <paramref name="positions"/>, looking <paramref name="lookAhead"/> yalms ahead.</summary>
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

    /// <summary>The most the speed may change from one 60 fps frame to the next, in yalms a second: a fifth of the default speed.</summary>
    private const float SpeedJumpLimit = 1f;

    /// <summary>The fastest the camera may turn, in radians a second: 2,000°/s, about 33° a frame. Faster reads as a cut.</summary>
    private const float TurnLimit = 2000f * Deg;

    /// <summary>The most the turn rate may change from one 60 fps frame to the next, in radians a second: 720°/s, a turn lurching from still to 12° a frame.</summary>
    private const float TurnJumpLimit = 720f * Deg;

    /// <summary>Half a turn in one 60 fps frame, 10,800°/s, with a little over for rounding: the most any one frame can turn.</summary>
    private const float HalfTurnInAFrame = 10801f * Deg;

    /// <summary>The most a check's count or measure may be.</summary>
    private static float Limit(Check check) =>
        check switch
        {
            Check.WellFormed or Check.Snaps => 0f,
            Check.Whips => PictureSpinLimit,
            Check.SpeedJump => SpeedJumpLimit,
            Check.TurnCap => TurnLimit,
            Check.TurnJump => TurnJumpLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
        };

    /// <summary>Whether a check counts rather than bounds.</summary>
    private static bool Counts(Check check) => check is Check.WellFormed or Check.Snaps;

    /// <summary>Every check a combination fails by design.</summary>
    private static readonly Declared[] DeclaredExceptions =
    [
        .. TurnsRound(LookAhead0),
        .. TurnsRound(LookAheadNear),
        .. TurnsRound(LookAheadFar),
    ];

    /// <summary>Direction of travel on the doubleback at <paramref name="aim"/>: one snap, turning up to half a turn in one frame.</summary>
    private static Declared[] TurnsRound(Aim aim)
    {
        const string reason =
            "facing a spot along a path that runs straight back, it turns round in one frame where the spot passes back through the camera";
        return
        [
            new(Doubleback, aim, Check.Snaps, 1f, reason),
            new(Doubleback, aim, Check.TurnCap, HalfTurnInAFrame, reason),
            new(Doubleback, aim, Check.TurnJump, HalfTurnInAFrame, reason),
        ];
    }

    /// <summary>Every check measured on <paramref name="run"/>, frame by frame.</summary>
    private static List<Measure> MeasureAll(Track track, Run run)
    {
        var h = FrameSeconds / 2;
        var frames = Every(FrameSeconds, 0.0, run.Duration).ToArray();
        // Each frame's speed and turn rate, measured across the frame and taken at its middle.
        var middles = Every(FrameSeconds, h, run.Duration - h).ToArray();
        var broken = frames.Where(t => WellFormed.FirstBroken(run.At(t)) is not null).ToArray();
        var snaps = run.Snaps();
        var measures = new List<Measure>
        {
            new(Check.WellFormed, broken.Length, broken.Length > 0 ? $"first at {broken[0]:0.###} s" : ""),
            new(Check.Snaps, snaps.Count, string.Join("; ", snaps)),
            AtPeak(Check.SpeedJump, LargestChangeAt(middles, t => run.Speed(t, h), (a, b) => MathF.Abs(b - a))),
            AtPeak(Check.TurnCap, LargestAt(middles, t => run.WorldTurnRate(t, h).Length())),
            AtPeak(Check.TurnJump, LargestChangeAt(middles, t => run.WorldTurnRate(t, h), (a, b) => (b - a).Length())),
        };
        // Recorded roll is authored, so only the other aims are held to the whip limit.
        if (track.Aim != AimMode.AimKeys)
            measures.Add(AtPeak(Check.Whips, run.LargestTwistAt()));
        return measures;
    }

    /// <summary>A measure from its peak, 0 where nothing was measured.</summary>
    private static Measure AtPeak(Check check, Peak peak) =>
        double.IsNaN(peak.Time) && !float.IsNaN(peak.Value)
            ? new Measure(check, 0f, "")
            : new Measure(check, peak.Value, $"at {peak.Time:0.###} s");

    /// <summary>How a check came out on a combination.</summary>
    private enum Status
    {
        Pass,
        Declared,
        Fail,
    }

    /// <summary>A check's measure on a combination, how it came out, and why.</summary>
    private sealed record Verdict(Measure Measure, Status Status, string Why);

    /// <summary>One combination's verdicts.</summary>
    private sealed record Outcome(Combination Combination, List<Verdict> Verdicts)
    {
        internal Status Status => Verdicts.Max(v => v.Status);

        internal float? Value(Check check) => Verdicts.FirstOrDefault(v => v.Measure.Check == check)?.Measure.Value;
    }

    /// <summary>How <paramref name="measure"/> comes out on <paramref name="combination"/>: within its limit, or failing as declared.</summary>
    private static Verdict Judge(Combination combination, Measure measure)
    {
        var (check, value, limit) = (measure.Check, measure.Value, Limit(measure.Check));
        var declared = DeclaredExceptions.FirstOrDefault(d =>
            d.Shape == combination.Shape && d.Aim == combination.Aim && d.Check == check
        );
        if (declared is null)
            return value <= limit
                ? new Verdict(measure, Status.Pass, "")
                : new Verdict(
                    measure,
                    Status.Fail,
                    $"{check} {Shown(check, value)} over {Shown(check, limit)} {measure.Where}"
                );

        // A declared check must still fail, or its entry is out of date.
        var holds = Counts(check) ? value == declared.Expected : value > limit && value <= declared.Expected;
        return holds
            ? new Verdict(measure, Status.Declared, $"{check}: {declared.Reason}")
            : new Verdict(
                measure,
                Status.Fail,
                $"{check} {Shown(check, value)}, declared {Shown(check, declared.Expected)} {measure.Where}"
            );
    }

    /// <summary><paramref name="value"/> as the report shows it: counts as they are, speeds in yalms a second, the rest in degrees.</summary>
    private static string Shown(Check check, float value) =>
        check switch
        {
            Check.WellFormed or Check.Snaps => value.ToString("0", CultureInfo.InvariantCulture),
            Check.SpeedJump => value.ToString("0.###", CultureInfo.InvariantCulture),
            _ => (value / Deg).ToString("0.#", CultureInfo.InvariantCulture),
        };

    /// <summary>Plays and judges <paramref name="combination"/>.</summary>
    private static Outcome Play(Combination combination)
    {
        var track = combination.Track();
        var verdicts = MeasureAll(track, new Run(track)).Select(m => Judge(combination, m)).ToList();
        return new Outcome(combination, verdicts);
    }

    [Fact]
    public void EveryShapeUnderEveryAimPassesEveryCheckOrIsDeclared()
    {
        var outcomes = Combinations().AsParallel().AsOrdered().Select(Play).ToList();
        if (Environment.GetEnvironmentVariable(ReportVariable) == "1")
            WriteReport(outcomes);

        var problems = outcomes
            .SelectMany(o =>
                o.Verdicts.Where(v => v.Status == Status.Fail).Select(v => $"{o.Combination.Name}: {v.Why}")
            )
            .ToList();
        Assert.True(problems.Count == 0, $"{problems.Count} undeclared:\n{string.Join("\n", problems)}");
    }

    /// <summary>Writes one row per combination to a dated file under the test project's obj folder.</summary>
    private static void WriteReport(List<Outcome> outcomes)
    {
        Check[] columns = [Check.SpeedJump, Check.TurnCap, Check.TurnJump, Check.Whips, Check.Snaps];
        var text = new StringBuilder();
        text.AppendLine("# Movement sweep")
            .AppendLine()
            .AppendLine(
                CultureInfo.InvariantCulture,
                $"{outcomes.Count} combinations: {string.Join(", ", Enum.GetValues<Status>().Select(s => $"{outcomes.Count(o => o.Status == s)} {s.ToString().ToLowerInvariant()}"))}."
            )
            .AppendLine()
            .AppendLine(
                "| Combination | Speed jump (yalms/s) | Fastest turn (°/s) | Turn jump (°/s) | Whip (°/frame) | Snaps | Result | Reason |"
            )
            .AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var outcome in outcomes)
        {
            var figures = columns.Select(c => outcome.Value(c) is { } v ? Shown(c, v) : "–");
            var reasons = outcome.Verdicts.Where(v => v.Status != Status.Pass).Select(v => $"{v.Status}: {v.Why}");
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {outcome.Combination.Name} | {string.Join(" | ", figures)} | {outcome.Status.ToString().ToLowerInvariant()} | {string.Join("<br>", reasons).Replace("|", "\\|", StringComparison.Ordinal)} |"
            );
        }

        var folder = Path.Combine(RepositoryRoot(), "tests", "Vista.Tests", "obj", "sweep-reports");
        Directory.CreateDirectory(folder);
        var name = $"sweep-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md";
        File.WriteAllText(Path.Combine(folder, name), text.ToString());
    }
}
