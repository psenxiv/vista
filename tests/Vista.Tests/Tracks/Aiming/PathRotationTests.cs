using System.Numerics;
using CsCheck;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

namespace Vista.Tests.Tracks.Aiming;

public class PathRotationTests
{
    private static Quaternion At(float yaw, float pitch = 0f, float roll = 0f) =>
        CameraRotation.FromAngles(yaw, pitch, roll);

    private static float YawOf(Quaternion rotation) => CameraRotation.ToAngles(rotation).Yaw;

    /// <summary>How far either side of a middle point the world turn rate is sampled, in yalms on 1-yalm legs; the property scales it by the shorter leg beside each point. Close enough that the legs' own turn acceleration barely moves it (<see cref="RateAgreement"/> derives how much), and past <see cref="RateWindow"/> so each window stays on its own side.</summary>
    private const double RateOffset = 1e-3;

    /// <summary>The central-difference half-window each side's rate is measured over, in yalms on 1-yalm legs; the property scales it as <see cref="RateOffset"/>.</summary>
    private const double RateWindow = 1e-4;

    /// <summary>How far apart the world turn rate either side of a middle point may be and still count as one rate, in rad per yalm times L, the shorter leg beside the point (the channel scales with distance: legs k times longer turn k times slower). Every point's rate is its legs' rates mixed with positive weights summing to 1, then shortened, so it is at most π/L; it is also at most 3 times either leg's rate, at most 1.5 times it off that leg's axis (3·cos·sin), and never against it; an end point's is its leg's own. So on a leg s ≥ L long turning τ ≤ π, with the inverse left Jacobian scaling the rate across the turn by at most π/2, the rotation vector's second derivative at the point (PathRotation.At's names: (6·turn − 4·from − 2·to)/s² leaving, (2·from − 6·turn + 4·to)/s² arriving) is at most 6π along the turn and 6π + 1.5π² ≈ 33.7 leaving or 3π + 3π² ≈ 39.0 arriving across it, per L²; arriving adds the Jacobian's own change, at most 0.68·(π·π/2)² ≈ 16.6. That is 38.6 leaving and 59.9 arriving, per L², so at <see cref="RateOffset"/>·L each side reads at most 0.039/L and 0.060/L off the point's rate. Round-off: the rate is taken between the floats actually evaluated, so rounding the distance adds nothing; rounding the leg fraction (1.2e-7) moves the rotation by at most 3π·1.2e-7 ≈ 1.1e-6 rad, and the rest of At by a few float steps of a turn under 2π, so each evaluation is within 1.5e-6 rad and each side's rate within 1.5e-6 / (<see cref="RateWindow"/>·L) = 0.015/L. 0.039 + 0.060 + 2·0.015 ≈ 0.13; a search of 100,000 generated aims found at most 0.038.</summary>
    private const float RateAgreement = 0.13f;

    /// <summary>The world turn rate per yalm between the floats nearest <paramref name="at"/> ± <paramref name="window"/>, divided by the distance between those floats, so rounding the distance adds nothing.</summary>
    private static Vector3 RateBetweenFloats(PathRotation channel, double at, double window)
    {
        var (from, to) = ((float)(at - window), (float)(at + window));
        return WorldTurnRate(d => channel.At((float)d), (from + (double)to) / 2.0, (to - (double)from) / 2.0);
    }

    [Fact]
    public void ARecordedAimTrackTurnsAtOneRateThroughAMiddlePoint()
    {
        // The user's shot: east 45° up and upright, straight up with the picture's top to the north (-z), west 45° up
        // and upright, 2 yalms apart. The middle point's turn so far, going in, is the whole first leg's; its
        // rotation-vector rate must be mapped through the inverse left Jacobian of the exponential map there for the
        // camera's actual world turn rate arriving to equal the rate it leaves with, since only one rate can pass through
        // a point.
        var east = CameraRotation.FromBasis(
            Vector3.Normalize(new Vector3(1f, 1f, 0f)),
            Vector3.Normalize(new Vector3(-1f, 1f, 0f))
        );
        var up = CameraRotation.FromBasis(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));
        var west = CameraRotation.FromBasis(
            Vector3.Normalize(new Vector3(-1f, 1f, 0f)),
            Vector3.Normalize(new Vector3(1f, 1f, 0f))
        );
        var channel = new PathRotation([east, up, west], [0f, 2f, 4f]);

        var before = WorldTurnRate(d => channel.At((float)d), 2.0 - RateOffset, RateWindow);
        var after = WorldTurnRate(d => channel.At((float)d), 2.0 + RateOffset, RateWindow);

        Assert.True(
            Vectors.AngleBetween(before, after) <= 0.5f * Deg,
            $"Axis differs by {Vectors.AngleBetween(before, after) / Deg:0.###}°"
        );
        var (beforeSpeed, afterSpeed) = (before.Length(), after.Length());
        Assert.True(
            MathF.Abs(beforeSpeed - afterSpeed) <= 0.01f * ((beforeSpeed + afterSpeed) / 2f),
            $"Speed differs: {beforeSpeed:0.###} vs {afterSpeed:0.###} rad/yalm"
        );
    }

    [Fact]
    public void APointBetweenLargeLegsAboutFarApartAxesTurnsAtOneRate()
    {
        // Two 140° legs, 1 yalm each, about world axes 60° apart (X, then (0.5, 0, 0.866) in the XZ plane; perpendicular
        // axes would give the point no rate, since their turns' dot product is 0): the middle point's rotation-vector
        // rate is their average, 70°·(1.5, 0, 0.866) = (105°, 0, 60.6°) per yalm, off each leg's own axis, so the inverse
        // left Jacobian's cross terms move the Hermite endpoint slope well off it. The axis check is what catches a
        // wrong Jacobian factor: mutating it (2·angle·sin angle to 2·angle/sin angle) turns the two sides' axes 5.8°
        // apart, but leaves their lengths only 0.024 rad per yalm apart, inside the speed check's 0.05. Unmutated, the
        // Hermite curve's own endpoint curvature (2·from − 6·turn + 4·to either side, over the leg's squared length) puts
        // the axes 0.25° and the lengths 0.0016 rad per yalm apart (both measured), so 2° keeps clear margin both ways;
        // the speed check guards the rate's size.
        var turnX = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 140f * Deg);
        var turnTilted = Quaternion.CreateFromAxisAngle(new Vector3(0.5f, 0f, MathF.Sqrt(0.75f)), 140f * Deg);
        var rotations = new[] { Quaternion.Identity, turnX, Quaternion.Concatenate(turnX, turnTilted) };
        var channel = new PathRotation(rotations, [0f, 1f, 2f]);

        var before = WorldTurnRate(d => channel.At((float)d), 1.0 - RateOffset, RateWindow);
        var after = WorldTurnRate(d => channel.At((float)d), 1.0 + RateOffset, RateWindow);

        Assert.True(
            Vectors.AngleBetween(before, after) <= 2f * Deg,
            $"Axis differs by {Vectors.AngleBetween(before, after) / Deg:0.###}°"
        );
        Assert.True(
            MathF.Abs(before.Length() - after.Length()) <= 0.05f,
            $"Speed differs: {before.Length():0.###} vs {after.Length():0.###} rad/yalm"
        );
    }

    /// <summary>How far past point 1 <see cref="LeavingPointOne"/> samples, in yalms, at once and twice this.</summary>
    private const double LeavingOffset = 4e-3;

    /// <summary>The central-difference half-window <see cref="LeavingPointOne"/> measures over, in yalms: wide enough that a rotation's float round-off, about 1.2e-7 rad, reads as under 3e-5 rad/yalm.</summary>
    private const double LeavingWindow = 2e-3;

    /// <summary>How far <see cref="LeavingPointOne"/> may read from the rate it measures, in rad/yalm. Extrapolating from <see cref="LeavingOffset"/> and twice it cancels the leg's curvature and leaves the rate's second derivative, at most 12·0.1745 + 6·0.2618 + 6·0.1745 ≈ 4.7 rad/yalm³ on these 1-yalm legs of 10° between rates of at most 15° and 10°, times 1.6e-5 yalm²: 7.5e-5; round-off, tripled by extrapolating (2a − b), adds 9e-5. 5e-4 keeps margin, far below the 15°/yalm (0.26 rad/yalm) the cap sets.</summary>
    private const float LeavingAgreement = 5e-4f;

    /// <summary>The world turn rate per yalm leaving point 1, at distance 1, extrapolated from <see cref="LeavingOffset"/> and twice it past the point so a leg's own curvature cancels.</summary>
    private static Vector3 LeavingPointOne(PathRotation channel)
    {
        Vector3 At(double offset) => WorldTurnRate(d => channel.At((float)d), 1.0 + offset, LeavingWindow);
        return (2f * At(LeavingOffset)) - At(2.0 * LeavingOffset);
    }

    /// <summary>A rotation through the identity, 90° about x, then <paramref name="degrees"/> more about <paramref name="axis"/>, a yalm apart.</summary>
    private static PathRotation FastThenSlow(Vector3 axis, float degrees)
    {
        var first = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 90f * Deg);
        var second = Quaternion.Concatenate(first, Quaternion.CreateFromAxisAngle(axis, degrees * Deg));
        return new PathRotation([Quaternion.Identity, first, second], [0f, 1f, 2f]);
    }

    [Fact]
    public void ATurnBetweenLegsWhoseAxesAreSixtyDegreesApartIsCappedAtHalfTheFullCap()
    {
        // Legs turning 90°/yalm about x and 10°/yalm about (0.5, 0, 0.866), 60° away: cos 60° = 0.5, so the cap is
        // 3·10·0.5 = 15°/yalm, half the full 30. The three-point rate, ThroughWeights(1, 1) = (0.5, 0.5), is
        // 0.5·(90·x + 10·(0.5, 0, 0.866)) = (47.5, 0, 4.330)°/yalm, 47.70 long, so it is shortened to 15 along itself:
        // (14.938, 0, 1.3618)°/yalm = (0.26072, 0, 0.023767) rad/yalm. Leaving point 1 the turn so far is nothing, so
        // the world rate there is that rate.
        var leaving = LeavingPointOne(FastThenSlow(new Vector3(0.5f, 0f, MathF.Sqrt(0.75f)), 10f));

        Near(new Vector3(0.26072f, 0f, 0.023767f), leaving, LeavingAgreement);
    }

    [Fact]
    public void ATurnBetweenLegsWhoseAxesAreAtRightAnglesHasNoRate()
    {
        // Legs turning 90°/yalm about x and 10°/yalm about z: cos 90° = 0, so point 1 turns at no rate. (A hard switch
        // on the sign of the axes' dot product gave the full 3·10 = 30°/yalm here whenever float noise tipped it positive.)
        var leaving = LeavingPointOne(FastThenSlow(Vector3.UnitZ, 10f));

        Assert.InRange(leaving.Length(), 0f, LeavingAgreement);
    }

    [Fact]
    public void ALegWhoseTurnIsTooSmallToMeasureLeavesTheRotationFinite()
    {
        // TheAimNeverSteps' counterexample, cut to the rotation: the first leg rolls by −1.585e-26 rad, whose squared
        // size underflows, so the leg's turn rate measures 0 long while its dot product with the next leg's stays
        // positive. Point 1's cap is then 3·(that dot product)/(the larger rate): about 0, so the first leg barely
        // turns and halfway along it the camera still faces point 0's way, (0, 0, −1).
        var channel = new PathRotation(
            [At(0f), At(0f, 0f, -1.585e-26f), At(0f, 0.20689656f, -1.6255603f)],
            [0f, 11.826f, 57.703f]
        );

        Near(new Vector3(0f, 0f, -1f), CameraRotation.Forward(channel.At(5.913f)), 1e-6f);
        Assert.True(float.IsFinite(channel.At(30f).W));
    }

    /// <summary>A recorded aim through 3 to 6 points' angles (<see cref="AnyPoint"/>), at distances 0.1 to 10 yalms apart: the 100-to-1 neighbouring legs real tracks reach.</summary>
    private static readonly Gen<(ControlPoint[] Points, float[] Distances)> AnyRecordedAim =
        from points in AnyPoint.Array[3, 6]
        from legs in Gen.Float[0.1f, 10f].Array[points.Length - 1]
        select (points, Distances(legs));

    /// <summary>Distances along the path of points <paramref name="legs"/> apart, the first at 0.</summary>
    private static float[] Distances(float[] legs)
    {
        var distances = new float[legs.Length + 1];
        for (var leg = 0; leg < legs.Length; leg++)
            distances[leg + 1] = distances[leg] + legs[leg];
        return distances;
    }

    /// <summary>A generated aim as a channel's arguments, so a failing property prints something to paste into a test.</summary>
    private static string PrintAim((ControlPoint[] Points, float[] Distances) aim) =>
        $"[{string.Join(", ", aim.Points.Select(p => $"At({p.Yaw:R}f, {p.Pitch:R}f, {p.Roll:R}f)"))}], "
        + $"[{string.Join(", ", aim.Distances.Select(d => $"{d:R}f"))}]";

    [Fact]
    [Trait("Category", "Property")]
    public void ARecordedAimTurnsAtOneRateThroughEveryMiddlePoint()
    {
        AnyRecordedAim.Sample(
            aim =>
            {
                var channel = new PathRotation(
                    aim.Points.Select(p => At(p.Yaw, p.Pitch, p.Roll)).ToArray(),
                    aim.Distances
                );
                for (var point = 1; point < aim.Points.Length - 1; point++)
                {
                    var d = aim.Distances[point];
                    var shorter = MathF.Min(d - aim.Distances[point - 1], aim.Distances[point + 1] - d);
                    var before = RateBetweenFloats(channel, d - (RateOffset * shorter), RateWindow * shorter);
                    var after = RateBetweenFloats(channel, d + (RateOffset * shorter), RateWindow * shorter);
                    var gap = (before - after).Length() * shorter;
                    if (!(gap <= RateAgreement))
                        Assert.Fail(
                            $"Point {point}: turn rate differs by {gap:0.#####} rad per yalm times the shorter leg, {shorter:0.###}, either side of {d:0.###} yalms"
                        );
                }
            },
            iter: 2000,
            print: Kept<(ControlPoint[] Points, float[] Distances)>(PrintAim)
        );
    }

    [Fact]
    public void APureYawTurnsAtOneRateThroughAPointBetweenLegsOfDifferentLengths()
    {
        // Yaws 0, 1, 3 at distances 0, 2 and 5: legs of 1/2 and 2/3 rad per yalm, each weighted by the other leg's length,
        // so point 1 turns at (1/2·3 + 2/3·2) / 5 = 0.56667 rad per yalm, from either side, as a path channel does.
        var channel = new PathRotation([At(0f), At(1f), At(3f)], [0f, 2f, 5f]);
        var (left, right) = Slopes(d => YawOf(channel.At((float)d)), 2.0, 1e-3);

        Assert.Equal(1f, YawOf(channel.At(2f)), 1e-5f);
        Assert.Equal(0.56667f, left, 0.01f);
        Assert.Equal(0.56667f, right, 0.01f);
    }

    [Fact]
    public void TheLastPointTurnsAtItsLegsOwnRate()
    {
        // Yaws 0, 1, 3 at distances 0, 2 and 3: the last leg turns 2 rad over 1 yalm, so the last point turns at 2 rad
        // per yalm, whatever the leg before does.
        var channel = new PathRotation([At(0f), At(1f), At(3f)], [0f, 2f, 3f]);
        Assert.Equal(2f, Slopes(d => YawOf(channel.At((float)d)), 3.0, 1e-3).Left, 0.02f);
    }

    [Fact]
    public void ARotationNeedsAnIncreasingDistanceForEveryPoint()
    {
        Assert.Throws<ArgumentException>(() => new PathRotation([], []));
        Assert.Throws<ArgumentException>(() => new PathRotation([At(0f), At(1f)], [0f]));
        Assert.Throws<ArgumentException>(() => new PathRotation([At(0f), At(1f)], [1f, 1f]));
    }

    [Fact]
    public void ABlendFromSixtyToOneHundredAndTwentyDegreesPitchPassesStraightUp()
    {
        // Pitch 120° is yaw 180°, pitch 60°, roll 180°. The shortest turn from pitch 60° is 60° more about the camera's
        // right, and a lone leg eases symmetrically, so halfway it's at pitch 90°: facing straight up.
        var channel = new PathRotation([At(0f, 60f * Deg), At(MathF.PI, 60f * Deg, MathF.PI)], [0f, 2f]);

        Near(Vector3.UnitY, CameraRotation.Forward(channel.At(1f)), 1e-4f);
    }

    [Theory]
    [InlineData(1f, -1f)]
    [InlineData(-1f, 1f)]
    public void AHalfTurnOfYawTurnsTheWayTheYawDoes(float direction, float facingX)
    {
        // Yaw 0 to ±π is a half turn either way; it turns the way the angle does, so halfway it's at yaw ±π/2, facing
        // (∓1, 0, 0) by yaw = atan2(−x, −z).
        var channel = new PathRotation([At(0f), At(direction * MathF.PI)], [0f, 2f]);

        Near(new Vector3(facingX, 0f, 0f), CameraRotation.Forward(channel.At(1f)), 1e-4f);
    }

    [Fact]
    public void APointFacingStraightUpBlendsToItsNeighbour()
    {
        // From straight up to level at the same yaw 0.7 is a quarter turn about the camera's right, so halfway it
        // faces yaw 0.7, pitch 45°.
        var channel = new PathRotation([At(0.7f, MathF.PI / 2f), At(0.7f)], [0f, 2f]);
        var halfway = MathF.Sqrt(0.5f);

        Near(
            new Vector3(-MathF.Sin(0.7f) * halfway, halfway, -MathF.Cos(0.7f) * halfway),
            CameraRotation.Forward(channel.At(1f)),
            1e-4f
        );
        for (var d = 0f; d <= 2f; d += 0.05f)
            Assert.True(float.IsFinite(channel.At(d).W));
    }

    [Fact]
    public void BeforeTheFirstPointAndAfterTheLastItHoldsTheEnds()
    {
        var rotations = new[] { At(0f, 0.2f), At(1f) };
        var channel = new PathRotation(rotations, [1f, 3f]);

        Assert.Equal(rotations[0], channel.At(0f));
        Assert.Equal(rotations[1], channel.At(5f));
    }
}
