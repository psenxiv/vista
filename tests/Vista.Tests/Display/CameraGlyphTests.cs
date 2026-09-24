using System.Numerics;
using Vista.Core.Display;
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
}
