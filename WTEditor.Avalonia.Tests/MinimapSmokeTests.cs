using WTEditor.Application.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class MinimapSmokeTests
{
    [TestMethod]
    public void CoordinateConversions_CenterAxesBoundsAndRoundTrip()
    {
        Assert.AreEqual(new TilePoint(32, 32), MapCoordinates.PlacementToTile(0, 0));
        Assert.AreEqual(new TilePoint(31, 32), MapCoordinates.PlacementToTile(MapCoordinates.TileSize, 0));
        Assert.AreEqual(new TilePoint(32, 31), MapCoordinates.PlacementToTile(0, -MapCoordinates.TileSize));
        Assert.AreEqual(new TilePoint(32, 31), MapCoordinates.TerrainToTile(MapCoordinates.TileSize, 0));
        foreach (var tile in new[] { new TilePoint(0, 0), new TilePoint(64, 64), new TilePoint(12.25, 40.5) })
        {
            var placement = MapCoordinates.TileToPlacement(tile);
            var roundTrip = MapCoordinates.PlacementToTile(placement.X, placement.Z);
            Assert.AreEqual(tile.X, roundTrip.X, 0.000001);
            Assert.AreEqual(tile.Y, roundTrip.Y, 0.000001);
        }
        Assert.IsNull(MapCoordinates.PlacementBoundsToTile(double.NaN, 0, 1, 1));
        Assert.AreEqual(
            new System.Numerics.Vector3((float)MapCoordinates.ClientOriginOffset, (float)MapCoordinates.ClientOriginOffset, 10),
            MapCoordinates.TerrainToClient(new System.Numerics.Vector3(0, 0, 10)));
    }

    [TestMethod]
    public void MinimumZoom_FitsHalfTilePaddingOnAllFourSides()
    {
        using var model = new MinimapViewModel(new DeferredService());
        model.Zoom = MinimapViewModel.MinimumZoom;
        model.OffsetX = 100;
        model.OffsetY = -100;
        model.ClampOffsets();
        foreach (var offset in new[] { model.OffsetX, model.OffsetY })
        {
            Assert.AreEqual(0d, offset - 0.5 * model.Zoom / 64, 0.000001);
            Assert.AreEqual(1d, offset + 64.5 * model.Zoom / 64, 0.000001);
        }
    }

    [TestMethod]
    public async Task GlobalWmo_ProjectsXZAndLoadsGroupImages()
    {
        var bounds = MapCoordinates.PlacementBoundsToTile(4 * 533.333, 6 * 533.333, 2 * 533.333, 5 * 533.333);
        Assert.IsNotNull(bounds);
        Assert.AreEqual(34d, bounds.MinX, 0.000001);
        Assert.AreEqual(37d, bounds.MinY, 0.000001);
        Assert.AreEqual(36d, bounds.MaxX, 0.000001);
        Assert.AreEqual(38d, bounds.MaxY, 0.000001);
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        var load = model.LoadAsync(Map(1) with
        {
            Wdt = new WorldMapWdtMetadata(1, 0x1) { GlobalWmoBounds = bounds }
        });
        Assert.AreEqual(1, service.Requests.Count);
        service.Requests[0].Completion.SetResult(new MinimapDocument([], 0));
        await load;
        Assert.AreSame(bounds, model.GlobalWmoBounds);
        Assert.AreEqual(64d / 2.2d, model.Zoom, 0.000001);
        Assert.AreEqual(1d / 22, model.OffsetX + bounds.MinX * model.Zoom / 64, 0.000001);
        Assert.AreEqual(21d / 22, model.OffsetX + bounds.MaxX * model.Zoom / 64, 0.000001);
        await model.LoadAsync(null);
        Assert.IsNull(model.GlobalWmoBounds);
    }

    [TestMethod]
    public void PanLimits_AllowHalfATileBeyondEveryWorldEdge()
    {
        using var model = new MinimapViewModel(new DeferredService());
        foreach (var zoom in new[] { 1d, 8d, 64d })
        {
            model.Zoom = zoom;
            model.OffsetX = 100;
            model.OffsetY = -100;
            model.ClampOffsets();
            Assert.AreEqual(zoom / 128, model.OffsetX, 0.000001);
            Assert.AreEqual(1 - zoom - zoom / 128, model.OffsetY, 0.000001);
        }
    }

    [TestMethod]
    public void DoubleClickMapping_PreservesFractionalPositionAtAnyZoomAndPan()
    {
        TilePoint? navigation = null;
        using var model = new MinimapViewModel(new DeferredService(), point => navigation = point)
        {
            Zoom = 8,
            OffsetX = -2.5,
            OffsetY = -4
        };

        // Viewport coordinates are chosen to land 45% and 20% into tile (24, 36).
        model.NavigateAtViewportPoint(
            (-2.5 + 24.45 * 8 / 64) * 800,
            (-4 + 36.20 * 8 / 64) * 800,
            800);

        Assert.IsNotNull(navigation);
        Assert.AreEqual(24.45, navigation.Value.X, 0.000001);
        Assert.AreEqual(36.20, navigation.Value.Y, 0.000001);
    }

    [TestMethod]
    public void Overview_PlacesTileAtCorrectXYAndLeavesOtherTilesTransparent()
    {
        const int size = MinimapDocument.OverviewTileSize;
        var pixels = new byte[64 * size * 64 * size * 4];
        MinimapService.CopyOverviewTile(pixels, [10, 20, 30, 255], 1, 1, new WorldMapTile(3, 7));
        var start = (7 * size * 64 * size + 3 * size) * 4;
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 255 }, pixels[start..(start + 4)]);
        Assert.AreEqual((byte)0, pixels[start - 1]);
        var end = ((8 * size - 1) * 64 * size + 4 * size - 1) * 4;
        Assert.AreEqual((byte)255, pixels[end + 3]);
        Assert.AreEqual((byte)0, pixels[end + 4]);
    }

    [TestMethod]
    public void TilePaths_UseInternalDirectoryAndTwoDigitCoordinates()
    {
        Assert.AreEqual("world/minimaps/Azeroth/map00_09.blp",
            MinimapService.GetTilePath("Azeroth", new WorldMapTile(0, 9)));
        Assert.AreEqual("world/minimaps/Azeroth/map63_42.blp",
            MinimapService.GetTilePath("Azeroth", new WorldMapTile(63, 42)));
    }

    [TestMethod]
    public async Task SelectionChange_CancelsOldRequestAndDiscardsLateResult()
    {
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        var oldLoad = model.LoadAsync(Map(1));
        var newLoad = model.LoadAsync(Map(2));
        Assert.IsTrue(service.Requests[0].Token.IsCancellationRequested);
        var current = new MinimapDocument([], 2);
        service.Requests[1].Completion.SetResult(current);
        await newLoad;
        service.Requests[0].Completion.SetResult(new MinimapDocument([], 1));
        await oldLoad;
        Assert.AreSame(current, model.Document);
        StringAssert.Contains(model.Status, "Map 2");

        model.Zoom = 4;
        model.OffsetX = -1;
        await model.LoadAsync(null);
        Assert.IsNull(model.Document);
        Assert.AreEqual(MinimapViewModel.MinimumZoom, model.Zoom);
        Assert.AreEqual((1 - model.Zoom) / 2, model.OffsetX);
    }

    [TestMethod]
    public async Task FitMap_UsesFullWdtBoundsEvenWhenImagesAreMissing()
    {
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        var load = model.LoadAsync(Map(1) with
        {
            Wdt = new WorldMapWdtMetadata(1, 0)
            {
                ActiveTiles = [new(10, 20), new(13, 21)]
            }
        });
        service.Requests[0].Completion.SetResult(new MinimapDocument([], 2));
        await load;
        model.Zoom = 1;
        model.OffsetX = model.OffsetY = 0;
        model.ResetViewCommand.Execute(null);

        Assert.AreEqual(64d / 4.4d, model.Zoom, 0.00001);
        Assert.AreEqual(1d / 22, model.OffsetX + 10 * model.Zoom / 64, 0.00001);
        Assert.AreEqual(21d / 22, model.OffsetX + 14 * model.Zoom / 64, 0.00001);
        Assert.AreEqual(3d / 11, model.OffsetY + 20 * model.Zoom / 64, 0.00001);
        Assert.AreEqual(8d / 11, model.OffsetY + 22 * model.Zoom / 64, 0.00001);
    }

    [TestMethod]
    public async Task FitMap_SingleTileAtWorldEdgeFillsViewport()
    {
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        var load = model.LoadAsync(Map(1) with
        {
            Wdt = new WorldMapWdtMetadata(1, 0) { ActiveTiles = [new(63, 0)] }
        });
        service.Requests[0].Completion.SetResult(new MinimapDocument([], 1));
        await load;
        Assert.AreEqual(64d / 1.1d, model.Zoom, 0.00001);
        Assert.AreEqual(1d / 22, model.OffsetX + 63 * model.Zoom / 64, 0.00001);
        Assert.AreEqual(1d / 22, model.OffsetY, 0.00001);
    }

    [TestMethod]
    public async Task FitMap_IncludesActiveCameraPositionOutsideMapBounds()
    {
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        var load = model.LoadAsync(Map(1) with
        {
            Wdt = new WorldMapWdtMetadata(1, 0) { ActiveTiles = [new(10, 10)] }
        });
        service.Requests[0].Completion.SetResult(new MinimapDocument([], 1));
        await load;
        model.ActivePosition = new TilePoint(20.5, 22.25);
        model.ResetViewCommand.Execute(null);

        var markerX = model.OffsetX + model.ActivePosition.Value.X * model.Zoom / 64;
        var markerY = model.OffsetY + model.ActivePosition.Value.Y * model.Zoom / 64;
        Assert.IsTrue(markerX is >= 0 and <= 1);
        Assert.IsTrue(markerY is >= 0 and <= 1);
    }

    [TestMethod]
    public async Task EmptyWdt_DoesNotRequestImagery()
    {
        var service = new DeferredService();
        using var model = new MinimapViewModel(service);
        await model.LoadAsync(Map(1) with { Wdt = new WorldMapWdtMetadata(1, 0) });
        Assert.AreEqual(0, service.Requests.Count);
        StringAssert.Contains(model.Status, "no active terrain tiles");
    }

    private static WorldMapCatalogEntry Map(int id) => new(
        new WorldMapRecord(id, $"Map {id}", "Azeroth", (uint)id, 0, 0),
        new WorldMapWdtMetadata((uint)id, 0) { ActiveTiles = [new(3, 7)] });

    private sealed class DeferredService : IMinimapService
    {
        public List<(CancellationToken Token, TaskCompletionSource<MinimapDocument> Completion)> Requests { get; } = [];
        public Task<MinimapDocument> LoadAsync(WorldMapCatalogEntry map, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<MinimapDocument>();
            Requests.Add((cancellationToken, completion));
            return completion.Task;
        }
    }
}
