using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;
using HeaderFlags = WoWLib.Formats.WMO.Root.Chunks.HeaderFlags;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335LiquidQueryTests
{
    [TestMethod]
    public void TerrainQueriesBilinearHeightRatherThanTheRenderedDiagonal()
    {
        var parsed = WorldLiquidMeshBuilder.Build([Layer() with { Heightmap = [0, 0, 0, 8] }], new Catalog());
        Assert.AreEqual(1, parsed.QueryGrids.Length);
        // Halfway over both axes: bilinear gives 2; either mesh diagonal gives 0 or 4.
        var eye = new Vector3(-25f / 12, -25f / 12, 0);
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(parsed.QueryGrids, eye, out var liquid));
        Assert.AreEqual(13, (int)liquid.TypeId);
        Assert.AreEqual(2, liquid.Depth, .004f); // Native global float coordinate spills.
    }

    [TestMethod]
    public void TerrainLayerOrderHolesOffsetsAndFloorAdmissionAreIndependentOfDrawBounds()
    {
        var parsed = WorldLiquidMeshBuilder.Build([
            Layer() with { LiquidTypeId = 13, MinHeight = 5, XOffset = 2, YOffset = 3 },
            Layer() with { LiquidTypeId = 14, MinHeight = 9, XOffset = 2, YOffset = 3 }
        ], new Catalog());
        var eye = new Vector3(-3.5f * 25 / 6, -2.5f * 25 / 6, 4);
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(parsed.QueryGrids, eye, out var first));
        Assert.AreEqual(new Wrath335ViewerLiquid(13, 1), first);
        var grids = parsed.QueryGrids.ToArray();
        grids[0] = grids[0] with { Tiles = [0] };
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(grids, eye, out first));
        Assert.AreEqual(new Wrath335ViewerLiquid(14, 5), first);
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery(grids, eye, out _, 4.02f, true));
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(grids, eye, out _, 4.005f, true));
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery(grids, eye + new Vector3(25f / 6, 0, 0), out _));
    }

    [TestMethod]
    [DataRow(false, 5f, false)]
    [DataRow(true, 5f, true)]
    [DataRow(true, 5.005f, true)]
    [DataRow(true, 5.02f, false)]
    [DataRow(false, 4.9f, true)]
    public void WmoTypeFlagControlsStrictHeightTolerance(bool tolerance, float z, bool present)
    {
        var grid = WmoGrid() with { TypeFlags = tolerance ? 4u : 0u };
        Assert.AreEqual(present, Wrath335LiquidQuery.TryQuery([grid], new(1, 1, z), out var liquid));
        if (present) Assert.AreEqual(5 - z, liquid.Depth);
    }

    [TestMethod]
    public void WmoUsesTileLowNibbleAndOriginalGridBeforePortalClipping()
    {
        var grid = WmoGrid() with { Heights = [0, 0, 0, 8], Tiles = [0x80] };
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery([grid], new(25f / 12, 25f / 12, 0), out var liquid));
        Assert.AreEqual(2, liquid.Depth, .000001f);
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery([grid with { Tiles = [0x8f] }], new(1, 1, 0), out _));
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery([grid], new(-.001f, 1, 0), out _));
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery([grid], new(25f / 6, 1, 0), out _));
    }

    [TestMethod]
    public void QueryOnlyWmoPayloadSurvivesUploadAndPreservesWholeGroupSentinel()
    {
        var parsed = WmoLiquidMeshBuilder.Build(new() { GroupLiquid = 13, RootFlags = (ushort)HeaderFlags.UseLiquidTypeDbcId }, new Catalog());
        Assert.IsTrue(parsed.IsEmpty);
        Assert.AreEqual(1, parsed.QueryGrids.Length);
        var uploaded = WorldLiquidLoader.Upload(default, parsed, 0);
        Assert.IsFalse(uploaded.HasGeometry);
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(uploaded.queryGrids, new(100, -100, 10), out var liquid));
        Assert.AreEqual(float.MaxValue, liquid.Depth);
        WorldLiquidLoader.Unload(ref uploaded, 0);
        Assert.IsNull(uploaded.queryGrids);
        Assert.IsFalse(Wrath335LiquidQuery.TryQuery([WmoGrid() with { LiquidTypeId = 0, Width = 0 }], Vector3.Zero, out _));
    }

    [TestMethod]
    public void PrimaryGroupMissDoesNotTryAnotherGroupAndCanSucceedOnTheNextFrame()
    {
        var groups = new WorldModelGroupBatches[] { new(), new() { liquid = new() { queryGrids = [WmoGrid()] } } };
        var model = new WorldModel { groupBatches = groups };
        Assert.AreEqual(default, WorldLiquidViewerQuery.QueryPrimary(model, 0, new(1, 1, 4)));
        Assert.AreEqual(default, WorldLiquidViewerQuery.QueryPrimary(model, -1, new(1, 1, 4)));
        groups[0] = new() { liquid = new() { queryGrids = [WmoGrid()] } };
        Assert.AreEqual(new Wrath335ViewerLiquid(17, 1), WorldLiquidViewerQuery.QueryPrimary(model, 0, new(1, 1, 4)));
    }

    [TestMethod]
    public void ExteriorQueryUsesMogiMaskAndTransformsSurfaceWhilePrimaryDepthStaysLocal()
    {
        var bounds = new BoundingBox(new(-10), new(10));
        var group = new WorldModelGroupBatches { liquid = new() { queryGrids = [WmoGrid()] }, mogiBoundingBox = bounds };
        var model = new WorldModel { groupBatches = [group with { mogiFlags = 0x2000 }, group] };
        var matrix = Matrix4x4.CreateScale(2) * Matrix4x4.CreateTranslation(0, 0, 30);
        var localEye = new Vector3(1, 1, 3);
        Assert.IsTrue(WorldLiquidViewerQuery.TryExterior(model, localEye, Vector3.Transform(localEye, matrix), matrix, out var liquid));
        Assert.AreEqual(new Wrath335ViewerLiquid(17, 4), liquid);
        Assert.AreEqual(new Wrath335ViewerLiquid(17, 2), WorldLiquidViewerQuery.QueryPrimary(model, 1, localEye));
        model.groupBatches = [group with { mogiFlags = 0x2000 }];
        Assert.IsFalse(WorldLiquidViewerQuery.TryExterior(model, localEye, Vector3.Transform(localEye, matrix), matrix, out _));
    }

    [TestMethod]
    public void LoadedTerrainViewerCrossesTheSurfaceAndRetriesAfterReloadWithoutVisibilityGates()
    {
        var parsed = WorldLiquidMeshBuilder.Build([Layer() with { MinHeight = 5 }], new Catalog());
        var tile = new ADTContainer(default, new MapTile { TileX = 32, TileY = 32 });
        var terrain = new Terrain { worldLiquid = new() { queryGrids = parsed.QueryGrids } };
        tile.OnLoaded(terrain);
        var floorQueries = 0;
        var query = new WorldLiquidViewerQuery(_ => { floorQueries++; return 0; });
        var eye = new Vector3(-1, -1, 4);
        var liquid = query.Locate(default, [], [tile], eye);
        Assert.AreEqual(new Wrath335ViewerLiquid(13, 1), liquid);
        Assert.AreEqual(Wrath335M2QueueMask.AboveWater, liquid.PassOrder.BeforeLiquid);
        // ID selects the underwater branch even in the allowed negative-depth tolerance band.
        liquid = query.Locate(default, [], [tile], eye with { Z = 5.005f });
        Assert.IsTrue(liquid.TypeId != 0 && liquid.Depth < 0);
        liquid = query.Locate(default, [], [tile], eye with { Z = 5.02f });
        Assert.AreEqual(default, liquid);
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, liquid.PassOrder.BeforeLiquid);
        Assert.AreEqual(2, floorQueries);
        tile.UpdateTerrain(default);
        Assert.AreEqual(default, query.Locate(default, [], [tile], eye));
        tile.OnLoaded(terrain);
        Assert.AreEqual(13, (int)query.Locate(default, [], [tile], eye).TypeId);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        ADTContainer[] tiles = [tile];
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++) query.Locate(default, Array.Empty<Container3D>(), tiles, eye);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    [TestMethod]
    public void OtherClientBuildersRetainTheirPreviousDrawPayloadWithoutNativeQueryGrids()
    {
        Assert.AreEqual(0, WorldLiquidMeshBuilder.Build([Layer()], new Catalog(false)).QueryGrids.Length);
        Assert.AreEqual(0, WmoLiquidMeshBuilder.Build(new()
        {
            XTiles = 1, YTiles = 1, XVertices = 2, YVertices = 2, Heights = [5, 5, 5, 5], Tiles = [0]
        }, new Catalog(false)).QueryGrids.Length);
    }

    [TestMethod]
    public void WmoViewerTypeAndToleranceStayOriginalWhenInteriorDrawMaterialRemapsToSeventeen()
    {
        var parsed = WmoLiquidMeshBuilder.Build(new()
        {
            GroupLiquid = 13, RootFlags = (ushort)HeaderFlags.UseLiquidTypeDbcId,
            XTiles = 1, YTiles = 1, XVertices = 2, YVertices = 2,
            Heights = [5, 5, 5, 5], Tiles = [0]
        }, new Catalog());
        Assert.AreEqual(17, (int)parsed.Materials[0].Key.LiquidTypeId);
        Assert.AreEqual(13, (int)parsed.QueryGrids[0].LiquidTypeId);
        Assert.AreEqual(4u, parsed.QueryGrids[0].TypeFlags);
        Assert.IsTrue(Wrath335LiquidQuery.TryQuery(parsed.QueryGrids, new(1, 1, 5.005f), out var liquid));
        Assert.AreEqual(13, (int)liquid.TypeId);
    }

    private static WorldLiquidLayerInput Layer() => new()
    {
        Width = 1, Height = 1, MinHeight = 5, LiquidTypeId = 13,
        VertexFormat = WorldLiquidVertexFormat.HeightDepth
    };
    private static WorldLiquidQueryGrid WmoGrid() => new(17, 0, true, Vector3.Zero, 1, 1, 2, [5, 5, 5, 5], [0]);
    private sealed class Catalog(bool native = true) : IWorldLiquidMaterialCatalog
    {
        public WorldLiquidMaterialDescriptor Resolve(ushort type, ushort format) =>
            new(new(type, format), WorldLiquidMaterialFamily.Water, Vector4.One, Vector4.One, 1, 0, 0, [])
            { Wrath335 = native ? new(1, 0, 1, 0, 1, 1000) : null, WmoTypeFlags = type == 13 ? 4u : 0u };
    }
}
