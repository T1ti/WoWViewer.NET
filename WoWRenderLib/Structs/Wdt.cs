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
    uint MinimapTexture);

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

    public uint FileDataId { get; init; }
    public uint Flags { get; init; }
    public uint TexFileDataId { get; init; }
    public bool HasSplitAdts { get; init; }
    public (Vector3 Min, Vector3 Max)? GlobalWmoExtents { get; init; }
    public WdtGlobalWmoPlacement? GlobalWmoPlacement { get; init; }
    public List<MapTile> Tiles { get; } = [];
    public Dictionary<(byte X, byte Y), MapFileDataIds> TileFiles { get; } = [];

    public bool TryGetTile(byte x, byte y, out MapFileDataIds files) =>
        TileFiles.TryGetValue((x, y), out files);

    public void Dispose() => Format?.Dispose();
}
