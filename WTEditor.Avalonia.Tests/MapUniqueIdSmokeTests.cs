using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Loaders;
using WoWRenderLib.Persistence;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MapUniqueIdSmokeTests
{
    [TestMethod]
    public void Scanner_ReadsOnlyMddfAndModfUniqueIds()
    {
        using var stream = BuildAdt(
            (FourCc("MVER"), 4, [0u]),
            (FourCc("MDDF"), 36, [12u, 42u]),
            (FourCc("MODF"), 64, [77u]));

        Assert.AreEqual(77u, MapUniqueIdScanner.ScanStream(stream));
    }

    [TestMethod]
    public void Store_ScansNewWdtModelOnceAndPersistsMapIdToMaximum()
    {
        var mapId = 987_654u;
        var root = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "map-unique-ids.json");
        var map = new WdtFile
        {
            FileDataId = 123_456u,
            HasSplitAdts = true
        };
        map.Tiles.Add(new MapTile { wdtFileDataID = map.FileDataId, tileX = 0, tileY = 0 });
        map.TileFiles[(0, 0)] = new MapFileDataIds(0, 555u, 0, 0, 0, 0, 0, 0);

        try
        {
            var scanCount = 0;
            var scan = MapUniqueIdStore.GetOrScan(
                mapId,
                map,
                cachePath,
                _ =>
                {
                    scanCount++;
                    using var stream = BuildAdt((FourCc("MODF"), 64, [15u, 91u]));
                    return MapUniqueIdScanner.ScanStream(stream);
                });

            Assert.AreEqual(91u, scan);
            Assert.AreEqual(1, scanCount);
            Assert.IsTrue(MapUniqueIdStore.TryGet(mapId, out var cached, cachePath));
            Assert.AreEqual(91u, cached);

            var json = File.ReadAllText(cachePath);
            StringAssert.Contains(json, $"\"{mapId}\"");
            StringAssert.Contains(json, "91");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static MemoryStream BuildAdt(params (uint Name, int RecordSize, uint[] UniqueIds)[] chunks)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (name, recordSize, uniqueIds) in chunks)
            {
                writer.Write(name);
                var isPlacement = name is var value && (value == FourCc("MDDF") || value == FourCc("MODF"));
                writer.Write(isPlacement
                    ? checked((uint)(recordSize * uniqueIds.Length))
                    : checked((uint)recordSize));

                if (isPlacement)
                {
                    foreach (var uniqueId in uniqueIds)
                    {
                        writer.Write(0u);
                        writer.Write(uniqueId);
                        for (var i = 0; i < recordSize - 8; i++)
                            writer.Write((byte)0);
                    }
                }
                else
                {
                    for (var i = 0; i < recordSize; i++)
                        writer.Write((byte)0);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static uint FourCc(string value) =>
        BitConverter.ToUInt32(Encoding.ASCII.GetBytes(value));
}
