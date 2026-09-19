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
    int InstanceType)
{
    /// <summary>
    /// Every column exposed by the active Map DB2 definition for this row.
    /// Keeping this collection dynamic lets the inspector follow build-specific
    /// fields without having to change the map-selection model for every client
    /// release.
    /// </summary>
    public IReadOnlyList<WorldMapDbSetting> Settings { get; init; } = [];
}

public sealed record WorldMapDbSetting(string Name, string Value, string TypeName);

public sealed record WorldMapWdtSetting(string Name, string Value);

/// <summary>
/// The data associated with one MAIN/MAID tile entry in a WDT.
/// </summary>
public sealed record WorldMapWdtTileData(WorldMapTile Position, uint Flags)
{
    public uint RootAdtFileDataId { get; init; }
    public uint Obj0AdtFileDataId { get; init; }
    public uint Obj1AdtFileDataId { get; init; }
    public uint Tex0AdtFileDataId { get; init; }
    public uint LodAdtFileDataId { get; init; }
    public uint MapTextureFileDataId { get; init; }
    public uint MapTextureNormalFileDataId { get; init; }
    public uint MinimapTextureFileDataId { get; init; }

    public bool HasTerrainFiles => RootAdtFileDataId != 0 || Obj0AdtFileDataId != 0;

    public bool HasFileData => RootAdtFileDataId != 0
        || Obj0AdtFileDataId != 0
        || Obj1AdtFileDataId != 0
        || Tex0AdtFileDataId != 0
        || LodAdtFileDataId != 0
        || MapTextureFileDataId != 0
        || MapTextureNormalFileDataId != 0
        || MinimapTextureFileDataId != 0;

    // MAIN flags identify legacy terrain tiles; modern WDTs identify them via
    // the root/obj MAID FileDataIDs instead.
    public bool IsActive => (Flags & 0x1) != 0 || HasTerrainFiles;
}

/// <summary>
/// The global WMO placement stored in a WDT MODF chunk.
/// </summary>
public sealed record WorldMapWdtGlobalWmoData
{
    public string Name { get; init; } = string.Empty;
    public uint NameFileDataId { get; init; }
    public uint UniqueId { get; init; }
    public float PositionX { get; init; }
    public float PositionY { get; init; }
    public float PositionZ { get; init; }
    public float RotationX { get; init; }
    public float RotationY { get; init; }
    public float RotationZ { get; init; }
    public float ExtentsMinX { get; init; }
    public float ExtentsMinY { get; init; }
    public float ExtentsMinZ { get; init; }
    public float ExtentsMaxX { get; init; }
    public float ExtentsMaxY { get; init; }
    public float ExtentsMaxZ { get; init; }
    public ushort Flags { get; init; }
    public ushort DoodadSet { get; init; }
    public ushort NameSet { get; init; }
    public ushort RawScale { get; init; }
    public float EffectiveScale { get; init; } = 1f;
}

/// <summary>
/// Cached WDT header, tile, and global-WMO data for a map.
/// </summary>
public sealed record WorldMapWdtMetadata(uint FileDataId, uint Flags)
{
    public uint Version { get; init; }
    public uint LegacyHeaderValue { get; init; }
    public IReadOnlyList<uint> LegacyHeaderUnusedValues { get; init; } = [];
    public IReadOnlyList<WorldMapWdtSetting> Settings { get; init; } = [];
    public IReadOnlyList<WorldMapWdtTileData> Tiles { get; init; } = [];
    public IReadOnlyList<WorldMapTile> ActiveTiles { get; init; } = [];
    public IReadOnlyDictionary<WorldMapTile, uint> MinimapTextureFileDataIds { get; init; } =
        new Dictionary<WorldMapTile, uint>();
    public string GlobalWmoName { get; init; } = string.Empty;
    public WorldMapWdtGlobalWmoData? GlobalWmo { get; init; }
    public TileBounds? GlobalWmoBounds { get; init; }
    public string? Error { get; init; }

    public bool IsAvailable => FileDataId != 0 && string.IsNullOrWhiteSpace(Error);

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

