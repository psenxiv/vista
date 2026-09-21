using System.Numerics;

namespace CinematicCam.Core.Camera;

/// <summary>Projects world points to screen pixels with a row-vector view-projection matrix, as the game does.</summary>
public static class ScreenProjection
{
    /// <summary>Pixel position of <paramref name="world"/> with the origin top-left, or null when it is behind the camera.</summary>
    public static Vector2? Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= float.Epsilon) return null;

        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        return new Vector2((x + 1f) * viewport.X * 0.5f, (1f - y) * viewport.Y * 0.5f);
    }
}
