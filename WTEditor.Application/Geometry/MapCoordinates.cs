using System.Numerics;

namespace WTEditor.Application.Geometry;

public readonly record struct TilePoint(double X, double Y);
public sealed record TileBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Pure conversions into continuous map tile coordinates; no UI or renderer dependencies.</summary>
public static class MapCoordinates
{
    /// <summary>Undo the generator's resolution choice: the first texture covers this square in world units.</summary>
    public static double WmoMinimapSpan(double width, double height) =>
        Math.Max(width, height) <= 16 ? 16 : Math.Max(width, height) <= 32 ? 32 :
        Math.Max(width, height) <= 64 ? 64 : 128;

    /// <summary>Model-local XYZ to placement X/up/Z, with right-handed Euler X/Y/Z rotations in degrees.</summary>
    public static TilePoint ModelToTile(Vector3 local, Vector3 position, Vector3 rotation, float scale)
    {
        // File-space MODF extents use (model Y, model Z, model X) at zero rotation.
        // PlacementToTile already reverses the ground axes; do not reverse them twice.
        var placementLocal = new Vector3(local.Y, local.Z, local.X) * scale;
        const float radians = MathF.PI / 180;
        var transform = Matrix4x4.CreateRotationX(rotation.X * radians)
            * Matrix4x4.CreateRotationY(rotation.Y * radians)
            * Matrix4x4.CreateRotationZ(rotation.Z * radians);
        var world = Vector3.Transform(placementLocal, transform) + position;
        return PlacementToTile(world.X, world.Z);
    }

    public const int TilesPerAxis = 64;
    public const double CenterTile = TilesPerAxis / 2d;
    public const double TileSize = 533.333;

    /// <summary>Terrain X is north/south, Y is west/east; tile X is horizontal.</summary>
    public static TilePoint TerrainToTile(double x, double y) =>
        new(CenterTile - y / TileSize, CenterTile - x / TileSize);

    /// <summary>
    /// Center-relative MDDF/MODF ground coordinates: X is west/east, Z is north/south.
    /// Y is elevation and is intentionally not an input. Zero projects to tile (32,32).
    /// This applies x' = 32*T - x and z' = 32*T - z, then divides by T.
    /// </summary>
    public static TilePoint PlacementToTile(double x, double z) =>
        new(CenterTile - x / TileSize, CenterTile - z / TileSize);

    /// <summary>Inverse of PlacementToTile, returning placement ground X/Z in world units.</summary>
    public static (double X, double Z) TileToPlacement(TilePoint point) =>
        ((CenterTile - point.X) * TileSize, (CenterTile - point.Y) * TileSize);

    /// <summary>Projects already-transformed MODF extents; do not add placement or rotate again.</summary>
    public static TileBounds? PlacementBoundsToTile(double x1, double z1, double x2, double z2)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(z1) || !double.IsFinite(x2) || !double.IsFinite(z2))
            return null;
        var first = PlacementToTile(x1, z1);
        var second = PlacementToTile(x2, z2);
        return new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
    }
}
