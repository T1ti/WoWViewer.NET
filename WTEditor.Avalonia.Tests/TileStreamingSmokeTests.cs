using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Streaming;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;
using CoreADTLoader = WoWRenderLib.Loaders.ADTLoader;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class TileStreamingSmokeTests
{
    [TestMethod]
    public void DesiredTilesAreOrderedFromTheCameraOutward()
    {
        var available = new HashSet<(byte X, byte Y)>();
        for (byte x = 30; x <= 34; x++)
        for (byte y = 30; y <= 34; y++)
            available.Add((x, y));

        var tiles = TileStreamingPolicy.BuildDesiredTiles(123, 32, 32, 2, available);

        Assert.AreEqual(25, tiles.Count);
        Assert.AreEqual(new MapTile { wdtFileDataID = 123, tileX = 32, tileY = 32 }, tiles[0]);
        for (var index = 1; index < tiles.Count; index++)
        {
            Assert.IsTrue(
                DistanceSquared(tiles[index - 1], 32, 32) <= DistanceSquared(tiles[index], 32, 32));
        }
    }

    [TestMethod]
    public void UploadQueuePriorityRotatesAcrossFrames()
    {
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 3 },
            Enumerable.Range(0, 4)
                .Select(offset => SceneManager.GetUploadQueueIndex(0, offset))
                .ToArray());
        CollectionAssert.AreEqual(
            new[] { 3, 0, 1, 2 },
            Enumerable.Range(0, 4)
                .Select(offset => SceneManager.GetUploadQueueIndex(3, offset))
                .ToArray());
    }

    [TestMethod]
    public void RepeatedUploadRoundsRemainFair()
    {
        var order = new List<int>();
        var firstQueue = 0;
        for (var round = 0; round < 3; round++)
        {
            for (var offset = 0; offset < 4; offset++)
                order.Add(SceneManager.GetUploadQueueIndex(firstQueue, offset));
            firstQueue = SceneManager.GetUploadQueueIndex(firstQueue, 1);
        }

        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 3, 1, 2, 3, 0, 2, 3, 0, 1 },
            order);
    }

    [TestMethod]
    public void StreamingPhasesLeaveBudgetForLaterWork()
    {
        Assert.AreEqual(3.5d, SceneManager.AllocatePhaseDeadline(0d, 10d, 0.35d), 0.001d);
        Assert.AreEqual(5.775d, SceneManager.AllocatePhaseDeadline(3.5d, 10d, 0.35d), 0.001d);
        Assert.AreEqual(10d, SceneManager.AllocatePhaseDeadline(12d, 10d, 0.35d), 0.001d);
    }

    [TestMethod]
    public void AlphaChannelCopyWritesOnlyTheSelectedInterleavedChannel()
    {
        var destination = Enumerable.Repeat((byte)0xCC, 16).ToArray();

        CoreADTLoader.CopyAlphaChannel([1, 2, 3, 4], destination, 2);

        CollectionAssert.AreEqual(
            new byte[]
            {
                0xCC, 0xCC, 1, 0xCC,
                0xCC, 0xCC, 2, 0xCC,
                0xCC, 0xCC, 3, 0xCC,
                0xCC, 0xCC, 4, 0xCC
            },
            destination);
    }

    [TestMethod]
    public void UnloadWaitsForGracePeriod()
    {
        var delay = TimeSpan.FromMilliseconds(750);
        var clock = new ManualTimeProvider();
        var tile = new MapTile { wdtFileDataID = 123, tileX = 32, tileY = 32 };
        var container = new ADTContainer(default, tile);

        container.ScheduleUnload(clock);
        clock.Advance(delay - TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(container.IsUnloadDue(clock, delay));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(container.IsUnloadDue(clock, delay));
    }

    [TestMethod]
    public void ReenteringDesiredRangeCancelsPendingUnload()
    {
        var delay = TimeSpan.FromMilliseconds(750);
        var clock = new ManualTimeProvider();
        var tile = new MapTile { wdtFileDataID = 123, tileX = 32, tileY = 32 };
        var container = new ADTContainer(default, tile);

        container.ScheduleUnload(clock);
        container.CancelUnload();
        clock.Advance(delay + TimeSpan.FromSeconds(1));

        Assert.IsFalse(container.IsUnloadScheduled);
        Assert.IsFalse(container.IsUnloadDue(clock, delay));
    }

    [TestMethod]
    public void RepeatedUnloadSchedulingDoesNotRestartGracePeriod()
    {
        var delay = TimeSpan.FromMilliseconds(750);
        var clock = new ManualTimeProvider();
        var tile = new MapTile { wdtFileDataID = 123, tileX = 31, tileY = 32 };
        var container = new ADTContainer(default, tile);

        container.ScheduleUnload(clock);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        container.ScheduleUnload(clock);
        clock.Advance(TimeSpan.FromMilliseconds(250));

        Assert.IsTrue(container.IsUnloadDue(clock, delay));
    }

    [TestMethod]
    public void TerrainEditBaselineIsCapturedOnlyWhenEditingStarts()
    {
        var tile = new MapTile { wdtFileDataID = 123, tileX = 32, tileY = 32 };
        var container = new ADTContainer(default, tile);
        container.OnLoaded(new Terrain
        {
            vertices =
            [
                new ADTVertex { Position = new Vector3(1, 2, 3) }
            ]
        });

        container.EnsureOriginalVerticesCaptured();
        var terrain = container.Terrain;
        terrain.vertices[0].Position.Z = 4;
        container.UpdateTerrain(terrain);
        container.RefreshModifiedState();

        Assert.IsTrue(container.IsModified);
        container.MarkSaved();
        Assert.IsFalse(container.IsModified);
    }

    private static int DistanceSquared(MapTile tile, int centerX, int centerY)
    {
        var x = tile.tileX - centerX;
        var y = tile.tileY - centerY;
        return x * x + y * y;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
