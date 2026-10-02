namespace WoWRenderLib.DX11.Renderer;

/// <summary>Compact sort key so liquid batches retain their large model matrices in place.</summary>
internal readonly record struct WorldLiquidSortKey(
    int VisibleIndex,
    float ViewDepth,
    int TileOrder,
    int ChunkIndex,
    int LayerIndex,
    WorldLiquidDrawPhase Phase = WorldLiquidDrawPhase.Late,
    uint NativeInstance = 0);

internal enum WorldLiquidDrawPhase { Early, Late }

internal static class WorldLiquidBatchOrdering
{
    public static int Compare(WorldLiquidSortKey left, WorldLiquidSortKey right)
    {
        var phase = left.Phase.CompareTo(right.Phase);
        if (phase != 0) return phase;
        // 12340: four-byte qsort entries compare the stored instance pointer
        // unsigned before the unreachable secondary keys of duplicate entries.
        if (left.NativeInstance != 0 || right.NativeInstance != 0)
            return left.NativeInstance.CompareTo(right.NativeInstance);
        var depth = right.ViewDepth.CompareTo(left.ViewDepth);
        if (depth != 0)
            return depth;
        var tile = left.TileOrder.CompareTo(right.TileOrder);
        if (tile != 0)
            return tile;
        var chunk = left.ChunkIndex.CompareTo(right.ChunkIndex);
        return chunk != 0 ? chunk : left.LayerIndex.CompareTo(right.LayerIndex);
    }
}
