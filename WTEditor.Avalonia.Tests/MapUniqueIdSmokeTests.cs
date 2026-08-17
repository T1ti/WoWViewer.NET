using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWFormatLib;
using WoWFormatLib.FileProviders;
using WoWFormatLib.Structs.ADT;
using WoWFormatLib.Structs.WDT;
using WoWRenderLib.Loaders;
using WoWRenderLib.Persistence;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MapUniqueIdSmokeTests
{
    [TestMethod]
    public void Scanner_ReadsOnlyMddfAndModfUniqueIds()
    {
        using var stream = BuildAdt(
            (ADTChunks.MVER, 4, [0u]),
            (ADTChunks.MDDF, 36, [12u, 42u]),
            (ADTChunks.MODF, 64, [77u]));

        Assert.AreEqual(77u, MapUniqueIdScanner.ScanStream(stream));
    }

    [TestMethod]
    public void Store_ScansMapOnceAndPersistsMapIdToMaximum()
    {
        var build = "map-unique-id-test-" + Guid.NewGuid().ToString("N");
        var fileDataId = 123_456u;
        var mapId = 987_654u;
        var root = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "map-unique-ids.json");

        try
        {
            FileProvider.SetProvider(
                new MemoryFileProvider(fileDataId, BuildAdt((ADTChunks.MODF, 64, [15u, 91u])).ToArray()),
                build);
            FileProvider.SetDefaultBuild(build);

            var map = new WDT
            {
                mphd = new MPHD { flags = MPHDFlags.wdt_has_maid },
                tiles = [(0, 0)],
                tileFiles = new Dictionary<(byte, byte), MapFileDataIDs>
                {
                    [(0, 0)] = new() { obj0ADT = fileDataId }
                }
            };

            Assert.AreEqual(91u, MapUniqueIdStore.GetOrScan(mapId, map, cachePath));
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

    private static MemoryStream BuildAdt(params (ADTChunks Name, int RecordSize, uint[] UniqueIds)[] chunks)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (name, recordSize, uniqueIds) in chunks)
            {
                writer.Write((uint)name);
                writer.Write(name is ADTChunks.MDDF or ADTChunks.MODF
                    ? checked((uint)(recordSize * uniqueIds.Length))
                    : checked((uint)recordSize));

                if (name is ADTChunks.MDDF or ADTChunks.MODF)
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

    private sealed class MemoryFileProvider(uint fileDataId, byte[] contents) : IFileProvider
    {
        public bool FileExists(uint filedataid) => filedataid == fileDataId;
        public Stream OpenFile(uint filedataid) => filedataid == fileDataId
            ? new MemoryStream(contents, writable: false)
            : throw new FileNotFoundException();
        public uint GetFileDataIdByName(string filename) => throw new NotSupportedException();
        public Stream OpenFile(string filename) => throw new NotSupportedException();
        public bool FileExists(string filename) => false;
        public Stream OpenFile(byte[] cKey) => throw new NotSupportedException();
        public bool FileExists(byte[] cKey) => false;
        public void SetBuild(string build) { }
    }
}
