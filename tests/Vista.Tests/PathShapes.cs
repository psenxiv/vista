using System.Numerics;

namespace Vista.Tests;

/// <summary>The path shapes the movement sweep and the camera regression scene play, as positions in yalms, +y up.</summary>
internal static class PathShapes
{
    /// <summary>The field of view every shape's points are recorded at, as the demo scene's points have.</summary>
    internal const float Fov = 0.78f;

    /// <summary>(0, 0, 0) to (30, 0, 0) in three legs.</summary>
    internal static Vector3[] Straight => [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f)];

    /// <summary>A level quarter circle of radius 20 about the origin, from +x round to +z, in five points.</summary>
    internal static Vector3[] GentleCurve => [.. Enumerable.Range(0, 5).Select(i => Round(20f, i * 22.5f))];

    /// <summary>Two opposite level quarter circles of radius 10, three points each and one shared: along +x curving to +z, then curving back to +x.</summary>
    internal static Vector3[] SCurve =>
        [
            new(0f, 0f, 0f),
            new(10f * Sin(45f), 0f, 10f - (10f * Cos(45f))),
            new(10f, 0f, 10f),
            new(20f - (10f * Cos(45f)), 0f, 10f + (10f * Sin(45f))),
            new(20f, 0f, 20f),
        ];

    /// <summary>15 yalms along +x, then a right angle and 15 along +z.</summary>
    internal static Vector3[] Corner => [new(0f, 0f, 0f), new(15f, 0f, 0f), new(15f, 0f, 15f)];

    /// <summary>Out 15 yalms along +x, across 3, and 15 back.</summary>
    internal static Vector3[] Hairpin => [new(0f, 0f, 0f), new(15f, 0f, 0f), new(15f, 0f, 3f), new(0f, 0f, 3f)];

    /// <summary>Out 15 yalms along +x and straight back 10.</summary>
    internal static Vector3[] Doubleback => [new(-8f, 0f, 0f), new(7f, 0f, 0f), new(-3f, 0f, 0f)];

    /// <summary>In along +x, up and over a loop 16 yalms high, and out along +x again, each point at least a yalm from the last.</summary>
    internal static Vector3[] Loop =>
        [
            new(-20f, 0f, 0f),
            new(-6f, 0f, 0f),
            new(4f, 3f, 0f),
            new(7f, 10f, 0f),
            new(0f, 16f, 0f),
            new(-7f, 10f, 0f),
            new(-4f, 3f, 0f),
            new(6f, 0f, 0f),
            new(20f, 0f, 0f),
        ];

    /// <summary>Level along -z, straight up 20 yalms, then level along +x, in legs of 10.</summary>
    internal static Vector3[] Crane =>
        [new(0f, -10f, 10f), new(0f, -10f, 0f), new(0f, 0f, 0f), new(0f, 10f, 0f), new(10f, 10f, 0f)];

    /// <summary>A quarter turn round a 10-yalm circle at a time, rising 3 yalms each: a steady climbing turn.</summary>
    internal static Vector3[] ClimbingTurn =>
        [
            .. Enumerable
                .Range(0, 9)
                .Select(i => new Vector3(
                    10f * MathF.Cos(i * MathF.PI / 2f),
                    3f * i,
                    10f * MathF.Sin(i * MathF.PI / 2f)
                )),
        ];

    /// <summary>Eight points 45° apart round a level circle of radius 12 about the origin, and a ninth back on the first.</summary>
    internal static Vector3[] Orbit => [.. Enumerable.Range(0, 8).Select(i => Round(12f, i * 45f)), Round(12f, 0f)];

    /// <summary>Along +x in legs of 1, 20, 1 and 20 yalms.</summary>
    internal static Vector3[] UnevenSpacing =>
        [new(0f, 0f, 0f), new(1f, 0f, 0f), new(21f, 0f, 0f), new(22f, 0f, 0f), new(42f, 0f, 0f)];

    /// <summary>Four level points each a yalm from the last, turning 60° at each middle point.</summary>
    internal static Vector3[] ClosePoints =>
        [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f + Cos(60f), 0f, Sin(60f)), new(1f, 0f, 2f * Sin(60f))];

    /// <summary>The user's recorded-aim shot's positions: along +x through the origin, 10 yalms a leg.</summary>
    internal static Vector3[] UpAndOver => [new(-10f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f)];

    /// <summary>The place <paramref name="degrees"/> round a level circle of <paramref name="radius"/> about the origin, from +x toward +z.</summary>
    private static Vector3 Round(float radius, float degrees) => new(radius * Cos(degrees), 0f, radius * Sin(degrees));

    private static float Sin(float degrees) => MathF.Sin(degrees * Fixtures.Deg);

    private static float Cos(float degrees) => MathF.Cos(degrees * Fixtures.Deg);
}
