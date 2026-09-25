using System.Numerics;
using Vista.Core.Display;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Display;

public class CameraGlyphTests
{
    [Fact]
    public void TheFaceSitsAheadAtDepthSizedByFovAndAspect()
    {
        // 90° vertical FoV at depth 1: half-height 1; aspect 2: half-width 2.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2f, 2f, 1f);

        Assert.Equal(Vector3.Zero, glyph.Apex);
        foreach (var corner in glyph.Corners)
            Assert.Equal(1f, corner.Z, 4);
        Assert.Equal(new[] { 1f, 1f, -1f, -1f }, glyph.Corners.Select(c => MathF.Round(c.Y, 4)));
        Assert.All(glyph.Corners, c => Assert.Equal(2f, MathF.Abs(c.X), 4));
    }

    [Fact]
    public void TheTabPointsUpFromTheMiddleOfTheTopEdge()
    {
        // 90 degrees at depth 1, aspect 1: half-height 1, so the tab is 1 tall x 0.5 and 1 wide x 0.35 either side.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2f, 1f, 1f);

        Assert.Equal(1.5f, glyph.TabTip.Y, 4);
        Assert.Equal(0f, glyph.TabTip.X, 4);
        Assert.Equal(1f, glyph.TabLeft.Y, 4);
        Assert.Equal(1f, glyph.TabRight.Y, 4);
        Assert.Equal(-0.35f, glyph.TabLeft.X, 4);
        Assert.Equal(0.35f, glyph.TabRight.X, 4);
    }

    [Fact]
    public void AWideFaceKeepsTheTabSizedByTheShorterHalfSpan()
    {
        // Aspect 2 makes the half-width 2, but the tab still follows the half-height of 1.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2f, 2f, 1f);

        Assert.Equal(1.5f, glyph.TabTip.Y, 4);
        Assert.Equal(0.35f, glyph.TabRight.X, 4);
    }

    [Fact]
    public void ARolledUpTurnsTheTabWithIt()
    {
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, MathF.PI / 2f, 1f, 1f);
        Assert.True(glyph.TabTip.X > 1f);
        Assert.Equal(0f, glyph.TabTip.Y, 4);
    }

    [Fact]
    public void AnUpAHairOffTheForwardStillTurnsTheTab()
    {
        // Up (1e-5, 0, 1) squared to forward +z leaves (1e-5, 0, 0), longer than the 1e-6 a square up falls back below, so
        // the tab points along +x: the face's top is 1 from the axis, and the tab rises half its span of 1 above that.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, new Vector3(1e-5f, 0f, 1f), MathF.PI / 2f, 1f, 1f);

        Near(new Vector3(1.5f, 0f, 1f), glyph.TabTip, 1e-4f);
    }

    [Fact]
    public void ItFollowsThePositionAndIgnoresUnnormalisedInputs()
    {
        var at = new Vector3(5f, 6f, 7f);
        var glyph = CameraGlyph.Build(at, Vector3.UnitZ * 3f, new Vector3(0f, 2f, 0.5f), MathF.PI / 2f, 1f, 1f);
        Assert.Equal(at, glyph.Apex);
        Near(at + new Vector3(0f, 0f, 1f), (glyph.Corners[0] + glyph.Corners[2]) / 2f, 1e-4f);
        Assert.True(glyph.TabTip.Y > at.Y + 1f);
    }

    [Fact]
    public void ANotANumberFovStaysFinite()
    {
        // The fallback half-angle is 0.39 rad, so at depth 1 the face sits tan(0.39) = 0.411055 above the axis.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, float.NaN, 1f, 1f);

        Assert.All(glyph.Corners, c => Assert.True(float.IsFinite(c.X) && float.IsFinite(c.Y)));
        Assert.Equal(0.411055f, glyph.Corners[0].Y, 4);
        Assert.Equal(0.616582f, glyph.TabTip.Y, 4);
    }

    [Fact]
    public void AnExtremeFovClampsToTheWidestHalfAngle()
    {
        // The half-angle clamps at 80 degrees, so at depth 1 the face sits tan(80) = 5.6713 above and below the axis.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI, 1f, 1f);

        Assert.Equal(5.6713f, glyph.Corners[0].Y, 4);
        Assert.Equal(5.6713f, glyph.Corners[1].Y, 4);
        Assert.Equal(-5.6713f, glyph.Corners[2].Y, 4);
        Assert.Equal(-5.6713f, glyph.Corners[3].Y, 4);
    }

    // An aim point gives the forward toward it, unnormalised, and the upright up rolled by the point's roll.

    [Fact]
    public void AGlyphFacesItsAimPointRolledByItsPoint()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f));
        var rolled = TrackEditing.Append(TrackEditing.Empty(), Point(0f, roll: MathF.PI / 2f));

        var (forward, up, fov) = CameraGlyph.Pose(track, 0, new Vector3(0f, 0f, -10f), Unused);
        Assert.Equal(new Vector3(0f, 0f, -10f), forward);
        Near(Vector3.UnitY, up, 1e-6f);
        Assert.Equal(1f, fov);

        // Turning π/2 about −Z is turning −π/2 about +Z, which takes (0, 1) to (sin π/2, cos π/2) = (1, 0).
        Near(Vector3.UnitX, CameraGlyph.Pose(rolled, 0, new Vector3(0f, 0f, -10f), Unused).Up, 1e-6f);

        // From (1, 2, 3) the aim point (1, 2, −7) is (0, 0, −10) away.
        var moved = TrackEditing.Append(TrackEditing.Empty(), Point(1f, 2f, 3f));
        Assert.Equal(new Vector3(0f, 0f, -10f), CameraGlyph.Pose(moved, 0, new Vector3(1f, 2f, -7f), Unused).Forward);
    }

    // An aim point closer than TrackAim.MinTargetDistance (0.1) gives no aim, so the recorded one is drawn.

    [Fact]
    public void AnAimPointOnTheCameraFallsBackToTheRecordedAim()
    {
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f, yaw: MathF.PI / 2f));

        // Forward (0, 0, −1) turned π/2 about Y is (−sin, 0, −cos) = (−1, 0, 0).
        var (forward, up, _) = CameraGlyph.Pose(track, 0, new Vector3(0.05f, 0f, 0f), Unused);

        Near(-Vector3.UnitX, forward, 1e-6f);
        Near(Vector3.UnitY, up, 1e-6f);
    }

    [Fact]
    public void RecordedAimNeverBuildsTheEvaluator()
    {
        var calls = 0;
        var track = TrackEditing.Append(TrackEditing.Empty(), Point(0f));

        var (forward, up, _) = CameraGlyph.Pose(
            track,
            0,
            null,
            () =>
            {
                calls++;
                return new TrackEvaluator(track);
            }
        );

        Near(-Vector3.UnitZ, forward, 1e-6f);
        Near(Vector3.UnitY, up, 1e-6f);
        Assert.Equal(0, calls);
    }

    // Direction of travel along +x at height 5 with no look ahead faces along the path, level.

    [Fact]
    public void DirectionOfTravelFacesAlongThePath()
    {
        var track = TrackEditing.SetLookAhead(
            TrackEditing.Append(
                TrackEditing.Append(TrackEditing.Empty(AimMode.PathTangent), Point(0f, 5f)),
                Point(10f, 5f)
            ),
            0f
        );
        var calls = 0;

        var (forward, up, _) = CameraGlyph.Pose(
            track,
            0,
            null,
            () =>
            {
                calls++;
                return new TrackEvaluator(track);
            }
        );

        Near(Vector3.UnitX, Vector3.Normalize(forward), 1e-4f);
        Near(Vector3.UnitY, up, 1e-4f);
        Assert.Equal(1, calls);
    }

    private static TrackEvaluator Unused() => throw new InvalidOperationException("The evaluator isn't needed here.");
}
