using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11;

public enum TerrainFlattenTarget { FixedHeight, BrushCenter }

public enum TerrainBrushMode
{
    Sculpt,
    Smooth,
    Flatten
}

public readonly record struct TerrainTileId(
    int MapId,
    int PositionIndex)
{
    public static TerrainTileId From(MapTile tile) =>
        new(tile.MapId, tile.PositionIndex);
}

public sealed record TerrainTileEdit(
    TerrainTileId Tile,
    uint RootAdtFileDataId,
    ADTVertex[] Before,
    ADTVertex[] After);

public sealed class TerrainStrokeDelta(IReadOnlyList<TerrainTileEdit> tiles)
{
    public IReadOnlyList<TerrainTileEdit> Tiles { get; } = tiles;
    public bool IsEmpty => Tiles.Count == 0;
}

public sealed record ModifiedTerrainTile(
    TerrainTileId Tile,
    uint RootAdtFileDataId,
    ADTVertex[] Vertices);
