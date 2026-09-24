using WoWLib;
using Formats = WoWLib.Formats;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Cache;

public static class WDTCache
{
    private const int TilesPerAxis = 64;
    private static readonly Dictionary<string, WdtFile> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static WdtFile GetOrLoad(uint fileDataId) => GetOrLoad(string.Empty, fileDataId);

    public static WdtFile GetOrLoad(string? path, uint fileDataId = 0)
    {
        var wdtPath = NormalizePath(path);
        var fileSystem = WowlibFileSystem.Current;
        var readsByFileDataId = fileSystem.Kind == StorageKind.Casc && fileDataId != 0;
        var cacheKey = readsByFileDataId || wdtPath.Length == 0
            ? $"__fdid_wdt/{fileDataId}"
            : wdtPath;
        if (Cache.TryGetValue(cacheKey, out var value))
            return value;

        var format = Formats.WDT.WDT.ForVersion(fileSystem.Version);
        using (var key = WowlibFileSystem.CreateReadKey(fileSystem, wdtPath, fileDataId))
            format.Read(fileSystem, key);

        var root = format.Root;
        var header = root.Header;
        var mapFileDataIds = GetMapFileDataIds(root);
        var hasSplitAdts = mapFileDataIds.Length > 0;
        if (wdtPath.Length == 0)
        {
            wdtPath = fileSystem.Kind == StorageKind.Mpq
                ? LegacyAssetIds.TryGetPath(fileSystem, fileDataId, out var legacyPath) ? legacyPath : string.Empty
                : MapAssetPathResolver.TryGetPath(fileDataId, out var modernPath) ? modernPath : string.Empty;
        }
        WdtGlobalWmoPlacement? globalWmoPlacement = null;
        if (root.GlobalWmo.Count > 0)
        {
            var placement = root.GlobalWmo[0];
            var globalWmoPath = root.GlobalWmoName.Empty ? string.Empty : root.GlobalWmoName.At(0);
            var globalWmoFileDataId = fileSystem.Kind == StorageKind.Mpq
                ? WowlibFileSystem.ResolveAssetId(fileSystem, globalWmoPath)
                : placement.NameId;
            var scale = (placement.Flags & (uint)Formats.Common.MapObjDefFlags.has_scale) != 0
                ? placement.Scale / 1024f
                : 1f;
            globalWmoPlacement = new WdtGlobalWmoPlacement(
                globalWmoFileDataId,
                new System.Numerics.Vector3(placement.Position.X, placement.Position.Y, placement.Position.Z),
                new System.Numerics.Vector3(placement.Rotation.X, placement.Rotation.Y, placement.Rotation.Z),
                scale,
                placement.UniqueId,
                (ushort)placement.Flags,
                placement.DoodadSet,
                placement.NameSet);
        }

        var wdt = new WdtFile
        {
            Path = wdtPath,
            FileDataId = fileSystem.Kind == StorageKind.Casc ? fileDataId : 0,
            Flags = header.Flags,
            TexFileDataId = GetTextureFileDataId(header),
            HasSplitAdts = hasSplitAdts,
            GlobalWmoExtents = root.GlobalWmo.Count > 0
                ? (new System.Numerics.Vector3(
                        root.GlobalWmo[0].Extents.Min.X,
                        root.GlobalWmo[0].Extents.Min.Y,
                        root.GlobalWmo[0].Extents.Min.Z),
                    new System.Numerics.Vector3(
                        root.GlobalWmo[0].Extents.Max.X,
                        root.GlobalWmo[0].Extents.Max.Y,
                        root.GlobalWmo[0].Extents.Max.Z))
                : null,
            GlobalWmoPlacement = globalWmoPlacement,
            Format = format
        };

        if (hasSplitAdts)
        {
            for (var index = 0; index < mapFileDataIds.Length; index++)
            {
                var files = mapFileDataIds[index];
                var x = (byte)(index % TilesPerAxis);
                var y = (byte)(index / TilesPerAxis);
                var ids = new MapFileDataIds(
                    files.RootAdt,
                    files.Obj0Adt,
                    files.Obj1Adt,
                    files.Tex0Adt,
                    files.LodAdt,
                    files.MapTexture,
                    files.MapTextureN,
                    files.MinimapTexture);

                wdt.TileFiles[MapTile.GetPositionIndex(x, y)] = ids;
                if (ids.RootAdt != 0 || ids.Obj0Adt != 0)
                {
                    wdt.Tiles.Add(new MapTile
                    {
                        WdtPath = wdtPath,
                        WdtFileDataId = wdt.FileDataId,
                        TileX = x,
                        TileY = y
                    });
                }
            }
        }
        else
        {
            var unresolvedTileCount = 0;
            for (var index = 0; index < root.Tiles.Count; index++)
            {
                if (root.Tiles[index].Flags == 0)
                    continue;

                var x = (byte)(index % TilesPerAxis);
                var y = (byte)(index / TilesPerAxis);
                wdt.Tiles.Add(new MapTile
                {
                    WdtPath = wdtPath,
                    WdtFileDataId = wdt.FileDataId,
                    TileX = x,
                    TileY = y
                });

                // Vanilla through Legion WDTs have MAIN but no MAID. Resolve
                // the ADT from the stable virtual path instead of treating the
                // missing modern FileDataID table as a missing tile.
                var adtPath = MapAssetPathResolver.GetLegacyAdtPath(wdtPath, x, y);
                var rootAdtFileDataId = fileSystem.Kind == StorageKind.Mpq
                    ? 0u
                    : MapAssetPathResolver.TryResolveFileDataId(adtPath, out var resolvedId) ? resolvedId : 0;
                if (rootAdtFileDataId != 0 || fileSystem.Exists(new FileKey(adtPath)))
                {
                    wdt.TileFiles[MapTile.GetPositionIndex(x, y)] = new MapFileDataIds(
                        rootAdtFileDataId, 0, 0, 0, 0, 0, 0, 0)
                    {
                        RootAdtPath = adtPath
                    };
                }
                else
                {
                    unresolvedTileCount++;
                }
            }

            if (unresolvedTileCount > 0)
            {
                Diagnostics.LoadDiagnostics.Warning(
                    $"WDT {fileDataId} has {unresolvedTileCount} active legacy tile(s) whose ADT paths " +
                    $"could not be resolved. WDT path: '{wdtPath}'.");
            }
        }

        Cache.Add(cacheKey, wdt);
        return wdt;
    }

    private static Formats.WDT.Root.Chunks.MapFileDataIDs[] GetMapFileDataIds(Formats.WDT.Root.WDTRoot root)
    {
        return root switch
        {
            Formats.WDT.Root.WDTRootBfa value => CopyMapFileDataIds(value.MapFdids.AsDataSpan()),
            Formats.WDT.Root.WDTRootShadowlandsPlus value => CopyMapFileDataIds(value.MapFdids.AsDataSpan()),
            _ => []
        };
    }

    private static Formats.WDT.Root.Chunks.MapFileDataIDs[] CopyMapFileDataIds(
        ReadOnlySpan<Formats.WDT.Root.Chunks.MapFileDataIDs.Data> source)
    {
        var result = new Formats.WDT.Root.Chunks.MapFileDataIDs[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            result[i] = new Formats.WDT.Root.Chunks.MapFileDataIDs
            {
                RootAdt = value.RootAdt,
                Obj0Adt = value.Obj0Adt,
                Obj1Adt = value.Obj1Adt,
                Tex0Adt = value.Tex0Adt,
                LodAdt = value.LodAdt,
                MapTexture = value.MapTexture,
                MapTextureN = value.MapTextureN,
                MinimapTexture = value.MinimapTexture
            };
        }

        return result;
    }

    private static uint GetTextureFileDataId(Formats.WDT.Root.Chunks.WDTHeader header) =>
        header is Formats.WDT.Root.Chunks.WDTHeaderBfaPlus modern ? modern.TexFdid : 0;

    public static void ReleaseWDT(uint fileDataId)
    {
        if (Cache.Remove($"__fdid_wdt/{fileDataId}", out var wdt))
            wdt.Dispose();
    }

    public static void ReleaseAll()
    {
        foreach (var wdt in Cache.Values)
            wdt.Dispose();
        Cache.Clear();
    }

    private static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
}
