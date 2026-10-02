using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Reflection;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Wrath335EntityLiquidTests
{
    [TestMethod]
    [DataRow(4f, 0xA0u, 0x40u)]
    [DataRow(5f, 0xE0u, 0x60u)]
    [DataRow(9f, 0xE0u, 0x60u)]
    public void WmoHitClassifiesSubmergedOrCrossingUsingInclusiveAuthoredMaximum(
        float maximum, uint expectedEntity, uint expectedLighting)
    {
        var cache = new Wrath335EntityLiquidCache { Flags = 0x1008, SunDiffuseMultiplier = .4f };
        cache.Query(new(1, 1, 100), new(new(0, 0, 0), new(2, 2, maximum)), [Wmo()]);
        Assert.AreEqual(0x1008u | expectedEntity, cache.Flags);
        Assert.AreEqual(5, cache.Height);
        Assert.AreEqual(.4f, cache.SunDiffuseMultiplier); // WMO doesn't run the terrain lighting reset.
        var plane = new Vector4(1, 2, 3, 4);
        var lighting = cache.Lighting(0x100, plane);
        Assert.AreEqual(0x100u | expectedLighting, lighting.Flags);
        Assert.AreEqual(expectedLighting == 0x60 ? new(0, 0, 1, -5) : plane, lighting.Plane);
    }

    [TestMethod]
    public void CachedMissAndHitDoNotReadChangedLinksUntilExplicitInvalidation()
    {
        var cache = new Wrath335EntityLiquidCache { Flags = 0x1060, Height = 13 };
        var box = new BoundingBox(Vector3.Zero, new(2, 2, 9));
        cache.Query(new(1, 1, 0), box, []);
        Assert.AreEqual(0x1080u, cache.Flags);
        cache.Query(new(1, 1, 0), box, [Wmo()]);
        Assert.AreEqual(13, cache.Height);
        Assert.AreEqual(0x20u, cache.Lighting().Flags);
        cache.Invalidate();
        cache.Query(new(1, 1, 0), box, [Wmo()]);
        Assert.AreEqual(5, cache.Height);
        cache.Query(new(1, 1, 0), box, [Wmo(8)]);
        Assert.AreEqual(5, cache.Height);
        cache.Invalidate();
        cache.Query(new(1, 1, 0), box, [Wmo(8)]);
        Assert.AreEqual(8, cache.Height);
        Assert.AreEqual(0x10E0u, cache.Flags);
    }

    [TestMethod]
    public void MissingGroupRetriesAndLaterHitCompletesTheCache()
    {
        var cache = new Wrath335EntityLiquidCache();
        var box = new BoundingBox(Vector3.Zero, new(2, 2, 9));
        var missing = new Wrath335EntityLiquidLink(false, false, default, Matrix4x4.Identity);
        cache.Query(new(1, 1, 0), box, [missing, Wmo() with { Grids = ReadOnlyMemory<WorldLiquidQueryGrid>.Empty }]);
        Assert.IsFalse(cache.Cached);
        Assert.AreEqual(0x20u, cache.Lighting().Flags);
        cache.Query(new(1, 1, 0), box, [missing, Wmo()]);
        Assert.IsTrue(cache.Cached);
        Assert.AreEqual(5, cache.Height);
    }

    [TestMethod]
    public void FirstLinkedLiquidWinsAndTerrainNodeEndsTraversalOnMiss()
    {
        var cache = new Wrath335EntityLiquidCache();
        var box = new BoundingBox(Vector3.Zero, new(2, 2, 9));
        cache.Query(new(1, 1, 0), box, [Wmo(8), Wmo(5)]);
        Assert.AreEqual(8, cache.Height);
        cache.Invalidate();
        cache.Query(new(1, 1, 0), box, [Wrath335EntityLiquidLink.ForTerrain([]), Wmo()]);
        Assert.AreEqual(0x80u, cache.Flags);
        Assert.AreEqual(8, cache.Height); // Retained height isn't a liquid-presence predicate.
    }

    [TestMethod]
    public void WmoTransformsTheBottomSampleAndSurfaceBackToWorldSpace()
    {
        var cache = new Wrath335EntityLiquidCache();
        var placement = Matrix4x4.CreateScale(2) * Matrix4x4.CreateTranslation(0, 0, 30);
        cache.Query(new(2, 2, 100), new(new(0, 0, 31), new(4, 4, 41)), [Wmo() with { Placement = placement }]);
        Assert.AreEqual(40, cache.Height);
        Assert.AreEqual(new Vector4(0, 0, 1, -40), cache.Lighting().Plane);
    }

    [TestMethod]
    public void TerrainHitClearsBit8AndResetsSunMultiplierWithoutFloorOrDrawGates()
    {
        var cache = new Wrath335EntityLiquidCache { Flags = 0x1008, SunDiffuseMultiplier = .2f };
        var link = Wrath335EntityLiquidLink.ForTerrain(TerrainGrids(5));
        cache.Query(new(-1, -1, 100), new(new(-2, -2, 0), new(0, 0, 10)), [link]);
        Assert.AreEqual(0x10E0u, cache.Flags);
        Assert.AreEqual(5, cache.Height);
        Assert.AreEqual(1, cache.SunDiffuseMultiplier);
    }

    [TestMethod]
    public void LiveTerrainAdapterUsesAuthoredBoundsAndInvalidatesOnMoveReloadAndUnload()
    {
        var tile = new ADTContainer(default, new MapTile { TileX = 32, TileY = 32 });
        tile.OnLoaded(new Terrain { worldLiquid = new() { queryGrids = TerrainGrids(5) } });
        var query = new WorldLiquidEntityQuery();
        var instance = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
        instance.FileDataId = 7;
        instance.Scale = 1;
        var packet = new M2InstancePacket([instance]);
        var model = new ParsedDoodadBatch
        {
            fileDataID = 7,
            boundingBox = new(new(-100), new(100)), boundingRadius = 100,
            wrath335Bounds = new(new(-1), new(1), 2), submeshes = []
        };
        Assert.IsTrue(packet.EnsureSpatialData(model));
        var group = new M2AnimationDrawGroup();
        group.AddInstance(0);
        packet.WorldMatrices[0] = Matrix4x4.CreateTranslation(-1, -1, 5);
        query.BeginFrame([tile]);
        var water = query.Prepare(packet, model, [group], Matrix4x4.Identity, false)!;
        Assert.AreEqual(new Wrath335M2WaterSelection(true, true), water[0].Selection);
        Assert.AreEqual(new Vector4(0, 0, 1, -5), water[0].WorldPlane);
        packet.WorldMatrices[0] = Matrix4x4.CreateTranslation(-1, -1, 9);
        var moved = query.Prepare(packet, model, [group], Matrix4x4.Identity, false)!;
        Assert.AreSame(water, moved); // Packet output is retained.
        Assert.AreEqual(new Wrath335M2WaterSelection(true, false), moved[0].Selection);
        tile.OnLoaded(new Terrain { worldLiquid = new() { queryGrids = TerrainGrids(20) } });
        query.BeginFrame([tile]);
        Assert.AreEqual(new Wrath335M2WaterSelection(false, true),
            query.Prepare(packet, model, [group], Matrix4x4.Identity, false)![0].Selection);
        tile.Unload();
        query.BeginFrame([tile]);
        Assert.AreEqual(new Wrath335M2WaterSelection(true, false),
            query.Prepare(packet, model, [group], Matrix4x4.Identity, false)![0].Selection);
        model.wrath335Bounds = null;
        Assert.IsNull(query.Prepare(packet, model, [group], Matrix4x4.Identity, false));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LiveWmoOwnerLinksIgnoreVisibilityAndRetryMissingGroups(bool missingGroup)
    {
        var cache = (Dictionary<uint, WorldModel>)typeof(WMOCache)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var deviceField = typeof(WMOCache).GetField("cachedDevice", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousDevice = deviceField.GetValue(null);
        var key = uint.MaxValue;
        while (cache.ContainsKey(key)) key--;
        WMOContainer? owner = null;
        try
        {
            var groups = new WorldModelGroupBatches[]
            {
                new() { doodadReferences = [0], liquid = new() { queryGrids = Wmo(5).Grids.ToArray() } },
                new() { doodadReferences = [0], liquid = new() { queryGrids = missingGroup ? null : Wmo(8).Grids.ToArray() } }
            };
            if (missingGroup) groups[0] = new() { doodadReferences = [0], liquid = new() { queryGrids = [] } };
            cache.Add(key, new() { rootWMOFileDataID = key, wrath335 = true, groupBatches = groups, doodadSets = [] });
            owner = new(default, key, 0) { ModelMatrix = Matrix4x4.Identity };
            Array.Fill(owner.EnabledGroups, false);
            var instance = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
            instance.FileDataId = 7;
            instance.ParentWMO = owner;
            instance.WmoDoodadIndex = 0;
            instance.ModelMatrix = Matrix4x4.CreateTranslation(1, 1, 5);
            var packet = new M2InstancePacket([instance]);
            var model = new ParsedDoodadBatch
            {
                fileDataID = 7, boundingBox = new(new(-100), new(100)), boundingRadius = 100,
                wrath335Bounds = new(new(-1), new(1), 2), submeshes = []
            };
            Assert.IsTrue(packet.EnsureSpatialData(model));
            var group = new M2AnimationDrawGroup();
            group.AddInstance(0);
            var query = new WorldLiquidEntityQuery();
            query.BeginFrame([]);
            var initial = query.Prepare(packet, model, [group], Matrix4x4.Identity, false)![0];
            Assert.AreEqual(missingGroup ? new(true, false) : new Wrath335M2WaterSelection(false, true), initial.Selection);
            // An unloaded owner group can appear in the same retained array.
            groups[1] = new() { doodadReferences = [0], liquid = new() { queryGrids = Wmo(5).Grids.ToArray() } };
            var next = query.Prepare(packet, model, [group], Matrix4x4.Identity, false)![0];
            Assert.AreEqual(missingGroup ? new(true, true) : new Wrath335M2WaterSelection(false, true), next.Selection);
            // Reload replaces the owned group generation and invalidates a completed hit.
            var replacement = cache[key];
            replacement.groupBatches = [groups[1]];
            cache[key] = replacement;
            Assert.AreEqual(new Wrath335M2WaterSelection(true, true),
                query.Prepare(packet, model, [group], Matrix4x4.Identity, false)![0].Selection);
        }
        finally
        {
            cache.Remove(key);
            if (owner != null) WMOCache.Release(key, 0);
            deviceField.SetValue(null, previousDevice);
        }
    }

    [TestMethod]
    public void SceneSideRetainsTheUpdateIdWhilePassOrderUsesTheRefreshedId()
    {
        var state = new Wrath335LiquidSceneState();
        state = state.Advance(new(17, -.005f));
        Assert.AreEqual(0, (int)state.SceneLiquidTypeId);
        Assert.AreEqual(Wrath335M2QueueMask.AboveWater, state.Viewer.PassOrder.BeforeLiquid);
        state = state.Advance(default);
        Assert.AreEqual(17, (int)state.SceneLiquidTypeId);
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, state.Viewer.PassOrder.BeforeLiquid);
        state = state.Advance(default);
        Assert.AreEqual(0, (int)state.SceneLiquidTypeId);
    }

    private static Wrath335EntityLiquidLink Wmo(float height = 5) => new(false, true,
        new WorldLiquidQueryGrid[] { new(17, 0, true, Vector3.Zero, 1, 1, 2,
            [height, height, height, height], [0]) }, Matrix4x4.Identity);

    private static WorldLiquidQueryGrid[] TerrainGrids(float height) =>
        [new(13, 0, false, Vector3.Zero, 1, 1, 2, [height, height, height, height], [1])];
}
