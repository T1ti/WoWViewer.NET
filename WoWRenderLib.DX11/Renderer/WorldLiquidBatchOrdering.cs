namespace WoWRenderLib.DX11.Renderer;

/// <summary>Compact sort key so liquid batches retain their large model matrices in place.</summary>
internal readonly record struct WorldLiquidSortKey(
    int VisibleIndex,
    float ViewDepth,
    int TileOrder,
    int ChunkIndex,
    int LayerIndex);

internal static class WorldLiquidBatchOrdering
{
    public static int Compare(WorldLiquidSortKey left, WorldLiquidSortKey right)
    {
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
