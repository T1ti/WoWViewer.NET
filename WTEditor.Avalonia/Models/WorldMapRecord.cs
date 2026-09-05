using WTEditor.Application.Geometry;

namespace WTEditor.Avalonia.Models;

/// <summary>
/// The small, UI-independent subset of a row from the client's Map DB2.
/// </summary>
public sealed record WorldMapRecord(
    int Id,
    string Name,
    string Directory,
    uint WdtFileDataId,
    int ExpansionId,
    int InstanceType);

/// <summary>
/// Cached WDT flags and active tile coordinates for a map.
/// </summary>
public sealed record WorldMapWdtMetadata(uint FileDataId, uint Flags)
{
    public IReadOnlyList<WorldMapTile> ActiveTiles { get; init; } = [];
    public TileBounds? GlobalWmoBounds { get; init; }

    // A WDT with the global-WMO flag set does not contain terrain tiles.
    public bool HasTerrain => (Flags & 0x1) == 0;
}

public sealed record WorldMapCatalogEntry(
    WorldMapRecord Map,
    WorldMapWdtMetadata Wdt)
{
    public bool HasTerrain => Wdt.HasTerrain;
}

public sealed record WorldMapTile(int X, int Y);

