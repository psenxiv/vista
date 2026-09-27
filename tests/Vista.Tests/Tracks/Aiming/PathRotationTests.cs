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

    /// <summary>How far either side of a middle point the world turn rate is sampled, in yalms: close enough that a leg's own curvature barely moves it (<see cref="RateAgreement"/> derives how much), far enough that float round-off in <see cref="TrackRuns.WorldTurnRate(Func{double, Quaternion}, double, double)"/>'s 1e-4-yalm window stays well under it.</summary>
    private const double RateOffset = 1e-3;

    /// <summary>The central-difference window <see cref="TrackRuns.WorldTurnRate(Func{double, Quaternion}, double, double)"/> measures each side's rate over.</summary>
    private const double RateWindow = 1e-4;

    /// <summary>How far apart, in rad per yalm, the world turn rate either side of a middle point may be and still count as one rate. Near the point, each side's rate leads or lags the point's own by about that leg's turn acceleration times <see cref="RateOffset"/>; from the Hermite curve's endpoint second derivative (2·from − 6·turn + 4·to, PathRotation.At's own names, over the leg's squared length), that acceleration is largest for a 1-yalm leg turning near π rad, and shrinks for gentler turns or longer legs (<see cref="AnyRecordedAim"/> generates no leg shorter or turn sharper). Float round-off in the distance adds to it: below 32 yalms a float step is at most 1.9e-6 yalm, so over <see cref="RateWindow"/>'s 2e-4 yalms each side's rate, at most about 3 rad per yalm here, can read up to 3 · 1.9e-6 / 2e-4 ≈ 0.03 off. A search of 100,000 generated aims found at most 0.033 rad per yalm. 0.08 keeps comfortable margin above that, and stays far below the several tenths a one-sided rate leaves.</summary>
    private const float RateAgreement = 0.08f;

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

    /// <summary>A recorded aim through 3 to 6 points' angles (<see cref="AnyPoint"/>), at distances 1 to 5 yalms apart. 1 yalm is the shortest leg <see cref="RateAgreement"/>'s derivation assumes, where the turn-rate gap it bounds is largest; 5 buys nothing beyond that (a longer leg only shrinks the gap) and keeps the distances below 32 yalms, where its round-off holds.</summary>
    private static readonly Gen<(ControlPoint[] Points, float[] Distances)> AnyRecordedAim =
        from points in AnyPoint.Array[3, 6]
        from legs in Gen.Float[1f, 5f].Array[points.Length - 1]
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
                    var before = WorldTurnRate(x => channel.At((float)x), d - RateOffset, RateWindow);
                    var after = WorldTurnRate(x => channel.At((float)x), d + RateOffset, RateWindow);
                    var diff = (before - after).Length();
                    if (!(diff <= RateAgreement))
                        Assert.Fail(
                            $"Point {point}: turn rate differs by {diff:0.#####} rad per yalm either side of {d:0.###} yalms"
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
