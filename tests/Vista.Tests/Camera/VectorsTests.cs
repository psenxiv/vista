using System.Numerics;
using Vista.Core.Camera;
using Xunit;
using static Vista.Tests.Fixtures;

namespace Vista.Tests.Camera;

public class VectorsTests
{
    [Fact]
    public void AngleBetweenIgnoresLength() =>
        Assert.Equal(QuarterTurn, Vectors.AngleBetween(new Vector3(3f, 0f, 0f), new Vector3(0f, 0.5f, 0f)), 1e-6f);

    [Fact]
    public void AngleBetweenNearlyEqualVectorsIsPrecise()
    {
        // (1, 0, 0) and (1, 1e-4, 0) are atan(1e-4) ≈ 1e-4 apart. Their unit dot, 1/√(1 + 1e-8), rounds to 1 in single
        // precision, so acos of it would read 0.
        Assert.Equal(1e-4f, Vectors.AngleBetween(Vector3.UnitX, new Vector3(1f, 1e-4f, 0f)), 1e-9f);
    }

    [Fact]
    public void AngleBetweenOppositeVectorsIsHalfATurn() =>
        Assert.Equal(MathF.PI, Vectors.AngleBetween(Vector3.UnitX, -Vector3.UnitX), 1e-6f);

    [Fact]
    public void SignedAngleIsPositiveTurningRightHandedAboutTheAxis()
    {
        // +x to −z turns right-handed about +y: (1, 0, 0) × (0, 0, −1) = (0, 1, 0), so +π/2; the other way, −π/2.
        Assert.Equal(QuarterTurn, Vectors.SignedAngle(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY), 1e-6f);
        Assert.Equal(-QuarterTurn, Vectors.SignedAngle(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), 1e-6f);
    }

    [Fact]
    public void NormalizeOrMakesAVectorUnitLengthOrFallsBack()
    {
        // (0, 3, 4) is 5 long.
        Near(new Vector3(0f, 0.6f, 0.8f), Vectors.NormalizeOr(new Vector3(0f, 3f, 4f), Vector3.UnitX), 1e-6f);
        Assert.Equal(Vector3.UnitX, Vectors.NormalizeOr(Vector3.Zero, Vector3.UnitX));
    }

    [Fact]
    public void NormalizeOrFallsBackAtOrBelowTheShortestLength()
    {
        Assert.Equal(Vector3.UnitX, Vectors.NormalizeOr(new Vector3(0f, 0f, 1e-5f), Vector3.UnitX, 1e-4f));
        Near(Vector3.UnitZ, Vectors.NormalizeOr(new Vector3(0f, 0f, 1e-3f), Vector3.UnitX, 1e-4f), 1e-6f);
    }

    [Fact]
    public void FlatOrKeepsTheLevelPartOrFallsBackStraightUpOrDown()
    {
        // (3, 7, 4)'s level part (3, 0, 4) is 5 long.
        Near(new Vector3(0.6f, 0f, 0.8f), Vectors.FlatOr(new Vector3(3f, 7f, 4f), Vector3.UnitX), 1e-6f);
        Assert.Equal(Vector3.UnitX, Vectors.FlatOr(new Vector3(0f, 5f, 0f), Vector3.UnitX));
    }

    [Fact]
    public void IsFiniteNeedsEveryComponentFinite()
    {
        Assert.True(Vectors.IsFinite(new Vector3(1f, -2f, 3f)));
        Assert.False(Vectors.IsFinite(new Vector3(1f, float.NaN, 3f)));
        Assert.False(Vectors.IsFinite(new Vector3(0f, 0f, float.NegativeInfinity)));
    }
}
