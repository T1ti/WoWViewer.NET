using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>MODF extent remapping verified at 0x7BF656–0x7BF6AE in build 12340.</summary>
public static class Wrath335WmoPlacementBounds
{
    public static BoundingBox FromClientExtents(Vector3 fileMin, Vector3 fileMax, Vector3 worldOffset) =>
        new(new(-fileMax.Z + worldOffset.X, -fileMax.X + worldOffset.Y, fileMin.Y + worldOffset.Z),
            new(-fileMin.Z + worldOffset.X, -fileMin.X + worldOffset.Y, fileMax.Y + worldOffset.Z));
}
