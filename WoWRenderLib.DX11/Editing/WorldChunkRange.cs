using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Editing;

/// <summary>
/// A loaded chunk selected by <see cref="WorldChunkRange.ForEachChunkInRange"/>.
/// The callback can operate in the chunk's local coordinate system while still
/// retaining the owning tile and the transforms needed to publish changes.
/// </summary>
public readonly record struct WorldChunkRangeContext<TTile, TChunk>(
    TTile Tile,
    TChunk Chunk,
    int ChunkIndex,
    Matrix4x4 ModelMatrix,
    Matrix4x4 InverseModelMatrix,
    Vector3 LocalCenter,
    float LocalRadius);

/// <summary>
/// Shared spatial iteration for editor operations over loaded world tiles.
/// Individual tools provide their tile/chunk accessors, so this is reusable for
/// terrain, liquids, foliage, navigation data, or future editor layers.
/// </summary>
public static class WorldChunkRange
{
    public static bool ForEachChunkInRange<TTile, TChunk>(
        IReadOnlyList<TTile> tiles,
        Vector3 worldPosition,
        float radius,
        Func<TTile, bool> isLoaded,
        Func<TTile, Matrix4x4> getModelMatrix,
        Func<TTile, BoundingBox> getTileBounds,
        Func<TTile, IReadOnlyList<TChunk>> getChunks,
        Func<TChunk, BoundingBox> getChunkBounds,
        Func<WorldChunkRangeContext<TTile, TChunk>, bool> visitChunk,
        Action<TTile>? markTileChanged = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(isLoaded);
        ArgumentNullException.ThrowIfNull(getModelMatrix);
        ArgumentNullException.ThrowIfNull(getTileBounds);
        ArgumentNullException.ThrowIfNull(getChunks);
        ArgumentNullException.ThrowIfNull(getChunkBounds);
        ArgumentNullException.ThrowIfNull(visitChunk);

        if (radius < 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var changed = false;
        for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
        {
            var tile = tiles[tileIndex];
            if (!isLoaded(tile))
                continue;

            var modelMatrix = getModelMatrix(tile);
            if (!Matrix4x4.Invert(modelMatrix, out var inverseModelMatrix))
                continue;

            var localCenter = Vector3.Transform(worldPosition, inverseModelMatrix);
            var localRadius = GetConservativeLocalRadius(radius, inverseModelMatrix);
            if (!Intersects(localCenter, getTileBounds(tile), localRadius))
                continue;

            var tileChanged = false;
            var chunks = getChunks(tile);
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                if (!Intersects(localCenter, getChunkBounds(chunk), localRadius))
                    continue;

                tileChanged |= visitChunk(new WorldChunkRangeContext<TTile, TChunk>(
                    tile,
                    chunk,
                    chunkIndex,
                    modelMatrix,
                    inverseModelMatrix,
                    localCenter,
                    localRadius));
            }

            if (tileChanged)
            {
                changed = true;
                markTileChanged?.Invoke(tile);
            }
        }

        return changed;
    }

    private static float GetConservativeLocalRadius(float radius, Matrix4x4 inverseModelMatrix)
    {
        if (radius == 0f)
            return 0f;

        var localX = Vector3.TransformNormal(new Vector3(radius, 0f, 0f), inverseModelMatrix).Length();
        var localY = Vector3.TransformNormal(new Vector3(0f, radius, 0f), inverseModelMatrix).Length();
        var localZ = Vector3.TransformNormal(new Vector3(0f, 0f, radius), inverseModelMatrix).Length();
        return MathF.Max(localX, MathF.Max(localY, localZ));
    }

    private static bool Intersects(Vector3 center, BoundingBox bounds, float radius)
    {
        var closestX = Math.Clamp(center.X, bounds.Min.X, bounds.Max.X);
        var closestY = Math.Clamp(center.Y, bounds.Min.Y, bounds.Max.Y);
        var deltaX = center.X - closestX;
        var deltaY = center.Y - closestY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }
}
