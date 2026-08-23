using WoWLib;
using Formats = WoWLib.Formats;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Cache;

public static class WDTCache
{
    private static readonly Dictionary<uint, WdtFile> Cache = [];

    public static WdtFile GetOrLoad(uint fileDataId)
    {
        if (Cache.TryGetValue(fileDataId, out var value))
            return value;

        var fileSystem = WowlibFileSystem.Current;
        var format = Formats.WDT.WDT.ForVersion(fileSystem.Version);
        format.Read(fileSystem, new FileKey(new FileDataId(fileDataId)));

        var root = format.Root;
        var header = root.Header;
        var mapFileDataIds = GetMapFileDataIds(root);
        var hasSplitAdts = mapFileDataIds.Length > 0;

        var wdt = new WdtFile
        {
            FileDataId = fileDataId,
            Flags = header.Flags,
            TexFileDataId = GetTextureFileDataId(header),
            HasSplitAdts = hasSplitAdts,
            Format = format
        };

        if (hasSplitAdts)
        {
            for (var index = 0; index < mapFileDataIds.Length; index++)
            {
                var files = mapFileDataIds[index];
                var x = (byte)(index % 64);
                var y = (byte)(index / 64);
                var ids = new MapFileDataIds(
                    files.RootAdt,
                    files.Obj0Adt,
                    files.Obj1Adt,
                    files.Tex0Adt,
                    files.LodAdt,
                    files.MapTexture,
                    files.MapTextureN,
                    files.MinimapTexture);

                wdt.TileFiles[(x, y)] = ids;
                if (ids.RootAdt != 0 || ids.Obj0Adt != 0)
                {
                    wdt.Tiles.Add(new MapTile
                    {
                        wdtFileDataID = fileDataId,
                        tileX = x,
                        tileY = y
                    });
                }
            }
        }
        else
        {
            for (var index = 0; index < root.Tiles.Count; index++)
            {
                if (root.Tiles[index].Flags == 0)
                    continue;

                var x = (byte)(index % 64);
                var y = (byte)(index / 64);
                wdt.Tiles.Add(new MapTile { wdtFileDataID = fileDataId, tileX = x, tileY = y });
            }
        }

        Cache.Add(fileDataId, wdt);
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
        if (Cache.Remove(fileDataId, out var wdt))
            wdt.Dispose();
    }

    public static void ReleaseAll()
    {
        foreach (var wdt in Cache.Values)
            wdt.Dispose();
        Cache.Clear();
    }
}
