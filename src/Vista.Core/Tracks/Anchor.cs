using System.Numerics;

namespace Vista.Core.Tracks;

/// <summary>A position and a yaw that points, tracks or a scene hang off; no pitch, roll or scale.</summary>
public readonly record struct Anchor(Vector3 Position, float Yaw)
{
    /// <summary>The world origin with yaw 0; changes nothing.</summary>
    public static readonly Anchor Origin = new(Vector3.Zero, 0f);

    /// <summary>Turns a horizontal offset by <paramref name="yaw"/> the way yaw turns the camera's look.</summary>
    public static Vector3 Turn(Vector3 offset, float yaw)
    {
        var cos = MathF.Cos(yaw);
        var sin = MathF.Sin(yaw);
        return new Vector3((offset.X * cos) + (offset.Z * sin), offset.Y, (offset.Z * cos) - (offset.X * sin));
    }

    /// <summary>Where a position local to this anchor sits in the world.</summary>
    public Vector3 ToWorld(Vector3 local) => Position + Turn(local, Yaw);

    /// <summary>A world position as seen from this anchor.</summary>
    public Vector3 ToLocal(Vector3 world) => Turn(world - Position, -Yaw);

    /// <summary>A point local to this anchor, placed in the world: moved, turned, and its yaw added to.</summary>
    public ControlPoint ToWorld(ControlPoint local) => local with { Position = ToWorld(local.Position), Yaw = local.Yaw + Yaw };

    /// <summary>A world point as seen from this anchor.</summary>
    public ControlPoint ToLocal(ControlPoint world) => world with { Position = ToLocal(world.Position), Yaw = world.Yaw - Yaw };

    /// <summary>An anchor local to this one, placed in the world.</summary>
    public Anchor ToWorld(Anchor child) => new(ToWorld(child.Position), child.Yaw + Yaw);

    /// <summary>A world anchor as seen from this one.</summary>
    public Anchor ToLocal(Anchor worldChild) => new(ToLocal(worldChild.Position), worldChild.Yaw - Yaw);
}
