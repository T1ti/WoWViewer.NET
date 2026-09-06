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
        // Keep this identical to WMOContainer.GetModelMatrix. The renderer consumes
        // unmodified WMO vertices, remaps placement axes in the translation, and
        // applies its final world-axis correction after the placement transform.
        const float radians = MathF.PI / 180;
        var transform = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationX(rotation.Z * radians)
            * Matrix4x4.CreateRotationY(rotation.X * radians)
            * Matrix4x4.CreateRotationZ((rotation.Y + 90f) * radians)
            * Matrix4x4.CreateTranslation(position.X, position.Z, position.Y)
            * Matrix4x4.CreateRotationZ(-270f * radians);
        var renderer = Vector3.Transform(local, transform);
        return TerrainToTile(renderer.X, renderer.Y);
    }

    public const int TilesPerAxis = 64;
    public const double CenterTile = TilesPerAxis / 2d;
    public const double TileSize = 533.333;
    public const double ClientOriginOffset = CenterTile * TileSize;

    /// <summary>Terrain X is north/south, Y is west/east; tile X is horizontal.</summary>
    public static TilePoint TerrainToTile(double x, double y) =>
        new(CenterTile - y / TileSize, CenterTile - x / TileSize);

    /// <summary>Center-origin renderer terrain coordinates to top-left-origin client coordinates.</summary>
    public static Vector3 TerrainToClient(Vector3 position) => new(
        (float)(ClientOriginOffset - position.X),
        (float)(ClientOriginOffset - position.Y),
        position.Z);

    public static Vector3 TerrainDirectionToClient(Vector3 direction) =>
        new(-direction.X, -direction.Y, direction.Z);

    /// <summary>
    /// Center-relative MDDF/MODF ground coordinates: X is west/east, Z is north/south.
    /// Y is elevation and is intentionally not an input. Zero projects to tile (32,32).
    /// This applies x' = 32*T - x and z' = 32*T - z, then divides by T.
    /// </summary>
    public static TilePoint PlacementToTile(double x, double z) =>
        new(CenterTile - x / TileSize, CenterTile + z / TileSize);

    /// <summary>Inverse of PlacementToTile, returning placement ground X/Z in world units.</summary>
    public static (double X, double Z) TileToPlacement(TilePoint point) =>
        ((CenterTile - point.X) * TileSize, (point.Y - CenterTile) * TileSize);

    /// <summary>Projects already-transformed MODF extents; do not add placement or rotate again.</summary>
    public static TileBounds? PlacementBoundsToTile(double x1, double z1, double x2, double z2)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(z1) || !double.IsFinite(x2) || !double.IsFinite(z2))
            return null;
        // MODF extents are stored in the already-oriented WMO bounds layout;
        // unlike the placement origin, their X axis corresponds to model Y.
        var first = new TilePoint(CenterTile + x1 / TileSize, CenterTile + z1 / TileSize);
        var second = new TilePoint(CenterTile + x2 / TileSize, CenterTile + z2 / TileSize);
        return new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
    }
}
