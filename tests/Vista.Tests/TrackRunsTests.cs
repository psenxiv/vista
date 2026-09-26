using System.Numerics;
using Vista.Core.Camera;
using Vista.Core.Tracks;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.TrackRuns;

namespace Vista.Tests;

public class TrackRunsTests
{
    [Fact]
    public void TwistIsZeroForAPureYawTurn()
    {
        // At pitch 0 the upright up is (0, 1, 0) for every yaw (sin 0 . sin yaw term vanishes), so both frames
        // share the same up and the picture does not turn.
        var forwardA = new Vector3(0f, 0f, -1f);
        var forwardB = new Vector3(-MathF.Sin(1f), 0f, -MathF.Cos(1f));
        var up = new Vector3(0f, 1f, 0f);

        Assert.Equal(0f, Twist(forwardA, up, forwardB, up), 1e-5f);
    }

    [Fact]
    public void TwistIsZeroForAPurePitchTurn()
    {
        // Going from pitch 0 to pitch 0.5 at yaw 0 is a rotation about world +X by 0.5 rad: forward (0,0,-1) ->
        // (0, sin 0.5, -cos 0.5), and the same rotation carries up (0,1,0) -> (0, cos 0.5, sin 0.5), which is
        // exactly the upright up at pitch 0.5 (cos 0.5 . sin 0 for X, cos 0.5 for Y, cos 0 . sin 0.5 for Z).
        var forwardA = new Vector3(0f, 0f, -1f);
        var upA = new Vector3(0f, 1f, 0f);
        var forwardB = new Vector3(0f, 0.479425539f, -0.877582562f);
        var upB = new Vector3(0f, 0.877582562f, 0.479425539f);

        Assert.Equal(0f, Twist(forwardA, upA, forwardB, upB), 1e-5f);
    }

    [Fact]
    public void TwistMeasuresARollAboutAFixedForward()
    {
        var forward = new Vector3(0f, 0f, -1f);
        var upA = new Vector3(0f, 1f, 0f);
        var upB = Vector3.Transform(upA, Quaternion.CreateFromAxisAngle(forward, 0.3f));

        Assert.Equal(0.3f, Twist(forward, upA, forward, upB), 1e-5f);
    }

    // Looking along -Z with up +Y until 0.5 s, then with an up that isn't a number.
    private static CameraState UpLostAtHalfASecond(double t) =>
        new(
            Vector3.Zero,
            new Vector3(0f, 0f, -10f),
            t < 0.5 ? Vector3.UnitY : new Vector3(float.NaN, float.NaN, float.NaN),
            1f
        );

    [Fact]
    public void AnUpThatIsNotANumberIsAPictureStep() =>
        Assert.Contains(new Run(UpLostAtHalfASecond, 1.0).PictureSteps(), step => float.IsNaN(step.Size));

    [Fact]
    public void AnUpThatIsNotANumberMakesTheLargestTwistNotANumber() =>
        Assert.True(float.IsNaN(new Run(UpLostAtHalfASecond, 1.0).LargestTwist()));

    [Fact]
    public void LargestAtGivesThePeakAndWhenItWasTaken()
    {
        // -(t - 0.5)² at 0, 0.25, 0.5, 0.75 and 1 s: -0.25, -0.0625, 0, -0.0625, -0.25, largest at 0.5 s.
        var peak = LargestAt(Every(0.25, 0.0, 1.0), t => -(float)((t - 0.5) * (t - 0.5)));

        Assert.Equal(0f, peak.Value, 1e-6f);
        Assert.Equal(0.5, peak.Time, 1e-9);
    }

    [Fact]
    public void LargestChangeAtGivesTheLaterTimeOfTheLargestChange()
    {
        // t² at 0, 0.25, 0.5, 0.75 and 1 s changes by 0.0625, 0.1875, 0.3125 and 0.4375, the last ending at 1 s.
        var peak = LargestChangeAt(Every(0.25, 0.0, 1.0), t => (float)(t * t), (a, b) => b - a);

        Assert.Equal(0.4375f, peak.Value, 1e-6f);
        Assert.Equal(1.0, peak.Time, 1e-9);
    }

    [Fact]
    public void AClockPlaysEachTimeReachedAndEndsAtTheEnd()
    {
        // A 1 s run stepped by 0.4 s: frames at 0, 0.4 and 0.8 s, then at the end, 1 s, rather than 1.2 s, then nothing.
        // The step is a float, 0.4 to within 6e-9.
        var clock = new Run(UpLostAtHalfASecond, 1.0).Clock();
        var times = Enumerable.Range(0, 4).Select(_ => clock(0.4f)!.Value.Time).ToArray();

        Assert.Equal(0.0, times[0], 1e-6);
        Assert.Equal(0.4, times[1], 1e-6);
        Assert.Equal(0.8, times[2], 1e-6);
        Assert.Equal(1.0, times[3], 1e-6);
        Assert.Null(clock(0.4f));
    }

    [Fact]
    public void ARunKnowsWhenItReachesAndLeavesEachPoint()
    {
        // Build3PointTrack's keys are at 0, 5 and 10 s; a 2 s hold on point 1 leaves it at 7 and reaches point 2 at 12.
        var run = new Run(TrackEditing.SetHold(Build3PointTrack(), 1, 2f));

        Assert.Equal(0.0, run.Depart(0), 1e-4);
        Assert.Equal(5.0, run.Arrive(1), 1e-4);
        Assert.Equal(7.0, run.Depart(1), 1e-4);
        Assert.Equal(12.0, run.Arrive(2), 1e-4);
        Assert.Equal(12.0, run.Depart(2), 1e-4);
    }

    [Fact]
    public void SpeedIsTheChordAcrossTheWindowOverItsSeconds()
    {
        // Round a circle of radius 5 at 2 rad/s, 10 yalms a second. Over t ± 0.01 s the chord is 2·5·sin(0.02), so the
        // speed read is 5·sin(0.02) / 0.01 = 9.99933, short of 10 by the arc's bow. Each position is rounded to a float,
        // within 2.4e-7 at radius 5, moving the chord by under 7e-7 and the speed by under 3.5e-5.
        var run = new Run(
            t => new CameraState(
                new Vector3(5f * (float)Math.Cos(2.0 * t), 0f, 5f * (float)Math.Sin(2.0 * t)),
                new Vector3(0f, 0f, -10f),
                Vector3.UnitY,
                1f
            ),
            2.0
        );

        Assert.Equal(9.99933f, run.Speed(0.7, 0.01), 1e-4f);
    }

    [Fact]
    public void HorizonTiltIsTheRightVectorsAngleFromLevel()
    {
        // Facing 45° up along -z, f = (0, a, -a) with a = √½, rolled by r = 0.3t: the upright up (0, a, a) and right
        // (1, 0, 0) turn about f, giving up (-sin r, a cos r, a cos r) and right f × up = (cos r, a sin r, a sin r). Its
        // angle from level is asin(a sin r): 0 at 0 s, where only pitch leans the picture, and asin(√½ sin 0.3) = 0.21052
        // at 1 s. Float round-off in the frame is about 1e-7.
        var a = MathF.Sqrt(0.5f);
        var run = new Run(
            t =>
            {
                var r = 0.3f * (float)t;
                return new CameraState(
                    Vector3.Zero,
                    new Vector3(0f, a, -a),
                    new Vector3(-MathF.Sin(r), a * MathF.Cos(r), a * MathF.Cos(r)),
                    1f
                );
            },
            1.0
        );

        Assert.Equal(0f, run.HorizonTilt(0.0), 1e-6f);
        Assert.Equal(0.21052f, run.HorizonTilt(1.0), 1e-5f);
    }

    [Fact]
    public void WorldTurnRateReadsASteadyTurnAboutTheWorldVertical()
    {
        // A frame tilted 1 rad about +x, then turned about the world's +y at 0.5 rad/s: q(t) = Y(0.5t)·B. Across t ± h,
        // q(t+h)·q(t−h)⁻¹ = Y(0.5(t+h))·B·B⁻¹·Y(−0.5(t−h)) = Y(0.5·2h), so the tilt cancels and the rate is (0, 0.5, 0),
        // positive by the right-hand rule about +y (carrying +z towards +x). Read in the frame's own axes it would lean
        // off +y by the 1 rad tilt. Each quaternion component rounds by a few 6e-8, moving the 0.01 rad turned over 2h =
        // 0.02 s by under 1e-6 rad, 5e-5 rad/s.
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f);
        Quaternion Rotation(double t) =>
            Quaternion.Concatenate(tilt, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f * (float)t));

        Near(new Vector3(0f, 0.5f, 0f), WorldTurnRate(Rotation, 0.7, 0.01), 1e-4f);
    }

    [Fact]
    public void CentringIsTheAngleBetweenTheFacingAndThePoint()
    {
        // At (1, 2, 3) facing -z: a point straight ahead is 0 off, and one 10 along +x and 10 along -z is 45° off.
        var run = new Run(
            _ => new CameraState(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, -7f), Vector3.UnitY, 1f),
            1.0
        );

        Assert.Equal(0f, run.Centring(0.5, new Vector3(1f, 2f, -20f)), 1e-6f);
        Assert.Equal(MathF.PI / 4f, run.Centring(0.5, new Vector3(11f, 2f, -7f)), 1e-6f);
    }
}
