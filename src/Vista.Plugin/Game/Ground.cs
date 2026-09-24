using System.Numerics;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;

namespace Vista.Plugin.Game;

/// <summary>Finds the ground under a world point: the first collision hit straight down, else the character's feet.</summary>
internal static class Ground
{
    private const float MaxDrop = 1000f;

    /// <summary>The ground's height under <paramref name="point"/>, the character's foot height when nothing is hit, or null.</summary>
    public static float? Below(Vector3 point)
    {
        if (BGCollisionModule.RaycastMaterialFilter(point, -Vector3.UnitY, out var hit, MaxDrop))
            return hit.Point.Y;
        return Plugin.ObjectTable.LocalPlayer?.Position.Y;
    }
}
