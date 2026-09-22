using System.Numerics;
using CinematicCam.Core.Editing;
using Xunit;

namespace CinematicCam.Tests.Editing;

public class CameraGlyphTests
{
    private static void Near(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void TheFaceSitsAheadAtDepthSizedByFovAndAspect()
    {
        // 90° vertical FoV at depth 1: half-height 1; aspect 2: half-width 2.
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2f, 2f, 1f);

        Assert.Equal(Vector3.Zero, glyph.Apex);
        foreach (var corner in glyph.Corners) Assert.Equal(1f, corner.Z, 4);
        Assert.Equal(new[] { 1f, 1f, -1f, -1f }, glyph.Corners.Select(c => MathF.Round(c.Y, 4)));
        Assert.All(glyph.Corners, c => Assert.Equal(2f, MathF.Abs(c.X), 4));
    }

    [Fact]
    public void TheTabPointsUpFromTheMiddleOfTheTopEdge()
    {
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2f, 1f, 1f);

        Assert.True(glyph.TabTip.Y > 1f);
        Assert.Equal(0f, glyph.TabTip.X, 4);
        Assert.Equal(1f, glyph.TabLeft.Y, 4);
        Assert.Equal(1f, glyph.TabRight.Y, 4);
        Assert.Equal(0f, glyph.TabLeft.X + glyph.TabRight.X, 4);
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
        Near(at + new Vector3(0f, 0f, 1f), (glyph.Corners[0] + glyph.Corners[2]) / 2f);
        Assert.True(glyph.TabTip.Y > at.Y + 1f);
    }

    [Fact]
    public void ANotANumberFovStaysFinite()
    {
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, float.NaN, 1f, 1f);
        Assert.All(glyph.Corners, c => Assert.True(float.IsFinite(c.X) && float.IsFinite(c.Y)));
        Assert.True(glyph.TabTip.Y > glyph.Corners[0].Y);
    }

    [Fact]
    public void AnExtremeFovStaysFinite()
    {
        var glyph = CameraGlyph.Build(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI, 1f, 1f);
        Assert.All(glyph.Corners, c => Assert.True(float.IsFinite(c.X) && float.IsFinite(c.Y)));
    }
}
