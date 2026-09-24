using System.Numerics;

namespace WoWRenderLib.Structs;

public readonly record struct MapFileDataIds(
    uint RootAdt,
    uint Obj0Adt,
    uint Obj1Adt,
    uint Tex0Adt,
    uint LodAdt,
    uint MapTexture,
    uint MapTextureN,
    uint MinimapTexture)
{
    // Legacy ADTs are path-addressed. This is a read hint and is deliberately
    // not part of any tile identity or indexing key.
    public string RootAdtPath { get; init; } = string.Empty;
}

public readonly record struct WdtGlobalWmoPlacement(
    uint FileDataId,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    uint UniqueId,
    ushort Flags,
    ushort DoodadSet,
    ushort NameSet);

public sealed class WdtFile : IDisposable
{
    internal IDisposable? Format { get; init; }

    public string Path { get; init; } = string.Empty;
    // Set only when the WDT was opened through a modern FileDataID. It is a
    // read hint propagated to tile loading, never a tile/index identity.
    public uint FileDataId { get; init; }
    public uint Flags { get; init; }
    public uint TexFileDataId { get; init; }
    public bool HasSplitAdts { get; init; }
    public (Vector3 Min, Vector3 Max)? GlobalWmoExtents { get; init; }
    public WdtGlobalWmoPlacement? GlobalWmoPlacement { get; init; }
    public List<MapTile> Tiles { get; } = [];
    public Dictionary<int, MapFileDataIds> TileFiles { get; } = [];

    public bool TryGetTile(byte x, byte y, out MapFileDataIds files) =>
        TileFiles.TryGetValue(MapTile.GetPositionIndex(x, y), out files);

    public void Dispose() => Format?.Dispose();
}
