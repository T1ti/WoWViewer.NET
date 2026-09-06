using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Services;
using WTEditor.Application.Geometry;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WmoMinimapSmokeTests
{
    [TestMethod]
    public void NamesAndTranslations_PreserveSourceGroupIndexAndDefaultOffsets()
    {
        const string root = @"WMO\Dungeon\KZ_Gnomeragon\KZ_Gnomeragon_Instance.wmo";
        var path = WmoMinimapLoader.GetTexturePath(root, 2);
        Assert.AreEqual("WMO/Dungeon/KZ_Gnomeragon/KZ_Gnomeragon_Instance_002_00_00.blp", path);
        var table = WmoMinimapLoader.ParseTranslations($"dir: dungeon\r\n{path.Replace('/', '\\')}\thashed.blp\r\n");
        Assert.AreEqual("hashed.blp", table[path.ToLowerInvariant()]);
        var candidates = WmoMinimapLoader.GetTextureCandidates(
            @"WORLD\WMO\DUNGEON\KZ_GNOMERAGON\KZ_GNOMERAGON_INSTANCE_CLASSIC.WMO", 0);
        Assert.AreEqual("WMO/DUNGEON/KZ_GNOMERAGON/KZ_GNOMERAGON_INSTANCE_CLASSIC_000_00_00.blp", candidates[0]);
        Assert.AreEqual("WMO/DUNGEON/KZ_GNOMERAGON/KZ_GNOMERAGON_INSTANCE_000_00_00.blp", candidates[1]);
        Assert.AreEqual("WMO/Dungeon/KZ_Gnomeragon/KZ_Gnomeragon_Instance_011_01_02.blp",
            WmoMinimapLoader.GetTexturePath(root, 11, 1, 2));
    }

    [DataTestMethod]
    [DataRow(16f, 16f, 1, 1)]
    [DataRow(128f, 128f, 1, 1)]
    [DataRow(128.1f, 128f, 2, 1)]
    [DataRow(128f, 128.1f, 1, 2)]
    [DataRow(182.2741f, 172.5895f, 2, 2)]
    [DataRow(400f, 200f, 4, 2)]
    public void GroupBounds_EnumerateEveryOffset(float width, float height, int columns, int rows)
    {
        var offsets = WmoMinimapLoader.GetTileOffsets(new Vector3(-10, -20, 0),
            new Vector3(width - 10, height - 20, 50)).ToArray();
        Assert.AreEqual(columns * rows, offsets.Length);
        Assert.AreEqual((0, 0), offsets.First());
        Assert.AreEqual((columns - 1, rows - 1), offsets.Last());
        Assert.AreEqual(offsets.Length, offsets.Distinct().Count());
    }

    [DataTestMethod]
    [DataRow(16d, 10d, 16d)]
    [DataRow(17d, 10d, 32d)]
    [DataRow(32d, 33d, 64d)]
    [DataRow(64d, 64d, 64d)]
    [DataRow(65d, 500d, 128d)]
    public void FirstTexture_FollowsGeneratorWorldSize(double width, double height, double expected) =>
        Assert.AreEqual(expected, MapCoordinates.WmoMinimapSpan(width, height));

    [TestMethod]
    public void ModelProjection_UsesGroundAxesScaleAndPlacementRotation()
    {
        var projected = MapCoordinates.ModelToTile(new Vector3(10, 20, 30), Vector3.Zero, Vector3.Zero, 2);
        Assert.AreEqual(32 + 40 / MapCoordinates.TileSize, projected.X, 0.000001);
        Assert.AreEqual(32 + 20 / MapCoordinates.TileSize, projected.Y, 0.000001);
        var rotated = MapCoordinates.ModelToTile(new Vector3(10, 0, 0), Vector3.Zero, new Vector3(0, 90, 0), 1);
        Assert.AreEqual(32 + 10 / MapCoordinates.TileSize, rotated.X, 0.000001);
        Assert.AreEqual(32d, rotated.Y, 0.000001);
    }

    [TestMethod]
    public void GnomereganModelCorners_AgreeWithNativeModfBounds()
    {
        // Observed in Classic WDT 782773 / WMO 5568954. MODF placement/rotation are zero.
        // Keep both native layouts as a regression fixture rather than deriving one from the other.
        var modf = MapCoordinates.PlacementBoundsToTile(-756.1609, 200.88834, 142.03816, 913.99976)!;
        var first = MapCoordinates.ModelToTile(new Vector3(200.88834f, -756.1609f, -331.8307f),
            Vector3.Zero, Vector3.Zero, 1);
        var last = MapCoordinates.ModelToTile(new Vector3(913.99976f, 142.03816f, -90.81402f),
            Vector3.Zero, Vector3.Zero, 1);
        Assert.AreEqual(modf.MinX, first.X, 0.000001);
        Assert.AreEqual(modf.MinY, first.Y, 0.000001);
        Assert.AreEqual(modf.MaxX, last.X, 0.000001);
        Assert.AreEqual(modf.MaxY, last.Y, 0.000001);
    }

    [TestMethod]
    public async Task WmoService_UsesGroupFootprintRatherThanWholeTerrainTile()
    {
        var loader = new TestLoader();
        var service = new MinimapService(loader);
        using var document = await service.LoadAsync(new WorldMapCatalogEntry(
            new WorldMapRecord(1, "Dungeon", "Dungeon", 42, 0, 1), new WorldMapWdtMetadata(42, 1)),
            CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(42u, loader.WdtId);
        Assert.AreEqual(0, document.Tiles.Count);
        Assert.AreEqual(4, document.WmoImages.Count);
        Assert.AreEqual(2, document.MissingTiles);
        var image = document.WmoImages[0];
        Assert.AreEqual(7, image.GroupIndex);
        // Each tile retains a 128-unit footprint, including the partially filled edge tiles.
        Assert.AreEqual(128 / MapCoordinates.TileSize, image.TopRight.Y - image.TopLeft.Y, 0.000001);
        Assert.AreEqual(-128 / MapCoordinates.TileSize, image.BottomLeft.X - image.TopLeft.X, 0.000001);
        var nextX = document.WmoImages.Single(tile => tile.OffsetX == 1 && tile.OffsetY == 0);
        var nextY = document.WmoImages.Single(tile => tile.OffsetX == 0 && tile.OffsetY == 1);
        Assert.AreEqual(image.TopRight, nextX.TopLeft);
        Assert.AreEqual(image.TopLeft, nextY.BottomLeft);
    }

    private sealed class TestLoader : IWmoMinimapLoader
    {
        public uint WdtId { get; private set; }
        public WmoMinimapData Load(uint wdtFileDataId, CancellationToken token)
        {
            WdtId = wdtFileDataId;
            return new(Vector3.Zero, Vector3.Zero, 1,
                [new(7, Vector3.Zero, new Vector3(200, 200, 50), [10, 20, 30, 255], 1, 1),
                 new(7, Vector3.Zero, new Vector3(200, 200, 50), [10, 20, 30, 255], 1, 1, 0, 1),
                 new(7, Vector3.Zero, new Vector3(200, 200, 50), [10, 20, 30, 255], 1, 1, 1, 0),
                 new(7, Vector3.Zero, new Vector3(200, 200, 50), [10, 20, 30, 255], 1, 1, 1, 1)], 2);
        }
    }
}
