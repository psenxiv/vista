using System.Numerics;

namespace Vista.Core.Camera;

/// <summary>Projects world points to screen pixels with a row-vector view-projection matrix, as the game does.</summary>
public static class ScreenProjection
{
    /// <summary>Pixel position of <paramref name="world"/> with the origin top-left, or null when it is behind the camera or nearer than <paramref name="nearW"/>.</summary>
    public static Vector2? Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport, float nearW = 0f)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        return clip.W <= float.Epsilon || clip.W < nearW ? null : ToPixels(clip, viewport);
    }

    /// <summary>Pixel end points of a segment cut to the part at least <paramref name="nearW"/> in front of the camera, or null when none of it is.</summary>
    public static (Vector2 Start, Vector2 End)? ProjectSegment(Vector3 start, Vector3 end, Matrix4x4 viewProjection, Vector2 viewport, float nearW)
    {
        var a = Vector4.Transform(new Vector4(start, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(end, 1f), viewProjection);
        if (a.W < nearW && b.W < nearW) return null;

        if (a.W < nearW) a = Vector4.Lerp(a, b, (nearW - a.W) / (b.W - a.W));
        else if (b.W < nearW) b = Vector4.Lerp(b, a, (nearW - b.W) / (a.W - b.W));

        return (ToPixels(a, viewport), ToPixels(b, viewport));
    }

    private static Vector2 ToPixels(Vector4 clip, Vector2 viewport)
    {
        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        return new Vector2((x + 1f) * viewport.X * 0.5f, (1f - y) * viewport.Y * 0.5f);
    }
}
