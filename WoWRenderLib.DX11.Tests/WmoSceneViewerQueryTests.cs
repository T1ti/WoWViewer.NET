using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WmoSceneViewerQueryTests
{
    private static readonly Vector3 Eye = new(0f, 0f, 1f);
    private static readonly BoundingBox Bounds = new(new(-10f, -10f, -2000f), new(10f, 10f, 10f));

    [TestMethod]
    public void EqualHitsFollowSceneInsertionOrderAcrossRepeatedAssets()
    {
        using var scene = new CpuScene();
        var first = scene.Add(Model(Floor(0f)));
        var middle = scene.Add(Model(Floor(0f)));
        var last = scene.Add(first.FileDataId);
        last.PlacementFlags = 0x420; // MODF flags must not become runtime flags.
        var result = scene.Locate();
        Assert.AreSame(last, result.Primary.Instance);
        scene.Buckets.Clear();
        scene.Buckets[(middle.FileDataId, "")] = [middle];
        scene.Buckets[(first.FileDataId, "")] = [first, last];
        Assert.AreSame(last, scene.Locate().Primary.Instance);
        scene.Objects.Reverse();
        Assert.AreSame(first, scene.Locate().Primary.Instance);
        Assert.AreEqual(0u, last.ViewerRuntimeFlags);
    }

    [TestMethod]
    public void ExteriorWinnerClearsInteriorAndBlocksLaterFartherGeometry()
    {
        using var scene = new CpuScene();
        scene.Add(Model(Floor(0f)));
        scene.Add(Model(Floor(0f, 8)));
        scene.Add(Model(Floor(-5f)));
        Assert.IsNull(scene.Locate().Primary.Instance);
        var last = scene.Add(Model(Floor(0f)));
        Assert.AreSame(last, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void QueryWithoutAHitKeepsTheEarlierWinner()
    {
        using var scene = new CpuScene();
        var first = scene.Add(Model(Floor(0f)));
        scene.Add(Model(Floor(0f, 0x410080)));
        Assert.AreSame(first, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void LiveTransformMovesPlacementIntoIndependentSecondaryPool()
    {
        using var scene = new CpuScene();
        var normal = scene.Add(Model(Floor(0f)));
        var updated = scene.Add(Model(Floor(0f)));
        updated.ModelMatrix = Matrix4x4.CreateTranslation(0f, 0f, 0.5f);
        var result = scene.Locate();
        Assert.AreEqual(0x400u, updated.ViewerRuntimeFlags);
        Assert.AreSame(normal, result.Primary.Instance);
        Assert.AreSame(updated, result.Secondary.Instance);
        normal.EnabledGroups[0] = false;
        result = scene.Locate();
        Assert.AreSame(updated, result.Primary.Instance);
        Assert.IsNull(result.Secondary.Instance);
    }

    [TestMethod]
    public void StoredFileBoundsControlBroadPhaseUntilATransformRebuildsThem()
    {
        using var scene = new CpuScene();
        var placement = scene.Add(Model(Floor(0f)));
        placement.InitializeFileViewerPlacement(new(new(100f, 100f, -10f), new(110f, 110f, 10f)));
        Assert.IsNull(scene.Locate().Primary.Instance);
        Assert.AreEqual(0, scene.TerrainQueries);
        placement.InvalidateTransform();
        placement.ModelMatrix = Matrix4x4.Identity;
        Assert.AreSame(placement, scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
    }

    [TestMethod]
    public void PortalOverrideChangesTheCapForFollowingPlacements()
    {
        using var scene = new CpuScene();
        scene.Add(Model(Floor(0f)));
        var portalModel = WithPortal(-0.15f);
        scene.Add(portalModel);
        var last = scene.Add(Model(Floor(-0.1f)));
        Assert.AreSame(last, scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
        scene.TerrainLimit = 0.5f;
        Assert.IsNull(scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
    }

    [TestMethod]
    public void PortalCanExtendPoolCapPastOriginalSegmentLength()
    {
        using var scene = new CpuScene();
        scene.Add(WithPortal(-1759.1f));
        // The face straddles the original segment's digest AABB, while its
        // intersection lies just past the endpoint. Native t uses the pool cap.
        var face = Floor(-1759.2f);
        face.raycastVertices[2].Z = -1758.9f;
        var next = scene.Add(Model(face));
        Assert.AreSame(next, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void SharedSceneResultsAssignSeedsOnlyToTheTwoSelectedPlacements()
    {
        using var scene = new CpuScene();
        var normal = scene.Add(WithPortal(-0.15f));
        var unselected = scene.Add(Model(Floor(-5f)));
        var updated = scene.Add(Model(Floor(0f)));
        updated.ModelMatrix = Matrix4x4.CreateTranslation(0f, 0f, 0.5f);
        var result = scene.Locate();
        Assert.IsTrue(result.UsesWrath335Rules);
        Assert.AreEqual(new WmoViewerGroups(1, 0), result.GetViewerGroups(normal));
        Assert.AreEqual(new WmoViewerGroups(0, -1), result.GetViewerGroups(updated));
        Assert.AreEqual(new WmoViewerGroups(-1, -1), result.GetViewerGroups(unselected));
        Assert.IsNull(default(WmoSceneViewerResult).GetViewerGroups(normal));
    }

    [TestMethod]
    public void OtherLegacyClientsRetainTheirExistingFirstHitTieRule()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f));
        model.wrath335 = false;
        var first = scene.Add(model);
        scene.Add(model);
        scene.Objects.Reverse();
        Assert.AreSame(first, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void ScenePortalPreparationReusesPrimaryMasksWithoutAnotherBspQuery()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f));
        model.portalGraphValid = true;
        var primary = scene.Add(model);
        var viewer = scene.Locate();
        primary.GetPortalVisibilityBuffers(model, out _, out _, out _, out var scratch);
        var epoch = scratch.ViewerBsp.Epoch;
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity);
        Assert.IsFalse(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out var traversed));
        Assert.IsTrue(applied);
        Assert.AreEqual(0, traversed);
        Assert.AreEqual(epoch, scratch.ViewerBsp.Epoch);
        preparation.Prepare(viewer, false, Eye, -Vector3.UnitZ, Matrix4x4.Identity);
        Assert.IsTrue(preparation.Views.HasSkyView);
        Assert.IsFalse(preparation.UsesWrath335Rules);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(primary, out _, out _));
    }

    [TestMethod]
    public void InvalidPrimaryPortalGraphKeepsSceneAndSkyVisible()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(Model(Floor(0f)));
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity);
        Assert.IsTrue(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.Views.HasExteriorView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out _));
        Assert.IsFalse(applied);
    }

    [TestMethod]
    public void ScenePortalPreparationDropsRootSkySeedWhenSecondaryExists()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f, 0x40));
        model.portalGraphValid = true;
        var primary = scene.Add(model);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity);
        Assert.AreEqual(WmoPortalRect.Full, preparation.Views.SkyRect);
        var updated = scene.Add(model);
        updated.InvalidateTransform();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity);
        Assert.IsFalse(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out _));
        Assert.IsTrue(applied);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(updated, out _, out _));
    }

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        legacyLighting = true, wrath335 = true, boundingBox = Bounds,
        groupBatches = groups, portals = [], doodads = [], doodadSets = [], wmoRenderBatches = []
    };

    private static WorldModelGroupBatches Floor(float height, uint flags = 0) => new()
    {
        flags = flags, mogiFlags = flags, boundingBox = Bounds, mogiBoundingBox = Bounds,
        raycastVertices = [new(-5f, -5f, height), new(5f, -5f, height), new(0f, 5f, height)],
        raycastIndices = [0, 1, 2], portalLinks = [], doodadReferences = [],
        viewerBsp = new([new(4, ushort.MaxValue, ushort.MaxValue, 1, 0, 0f)],
            [0], [0, 1, 2], [0])
    };

    private static WorldModel WithPortal(float height)
    {
        var owner = Floor(-1800f) with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }]
        };
        var model = Model(owner, Floor(-1800f));
        model.portals = [new()
        {
            Normal = Vector3.UnitZ, Distance = -height,
            Vertices = [new(-1f, -1f, height), new(1f, -1f, height),
                new(1f, 1f, height), new(-1f, 1f, height)]
        }];
        return model;
    }

    /// <summary>Seed decoded CPU models without constructing a device or loading assets.</summary>
    private sealed class CpuScene : IDisposable
    {
        private static readonly FieldInfo DeviceField = typeof(WMOCache).GetField(
            "cachedDevice", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _previousDevice = DeviceField.GetValue(null);
        private readonly Dictionary<uint, WorldModel> _cache = (Dictionary<uint, WorldModel>)
            typeof(WMOCache).GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private readonly List<uint> _keys = [];
        private readonly List<WMOContainer> _placements = [];
        private readonly WmoSceneViewerQuery _query;
        public readonly List<Container3D> Objects = [];
        public readonly Dictionary<(uint, string), List<WMOContainer>> Buckets = [];
        public float? TerrainLimit;
        public int TerrainQueries;

        public CpuScene() => _query = new(_ => { TerrainQueries++; return 1760f; });

        public WMOContainer Add(WorldModel model)
        {
            var key = uint.MaxValue - (uint)_keys.Count;
            while (_cache.ContainsKey(key))
                key--;
            _keys.Add(key);
            model.rootWMOFileDataID = key;
            _cache.Add(key, model);
            return Add(key);
        }

        public WMOContainer Add(uint key)
        {
            var placement = new WMOContainer(default, key, 0)
            {
                Scale = 1f, ModelMatrix = Matrix4x4.Identity
            };
            placement.InitializeFileViewerPlacement();
            _placements.Add(placement);
            Objects.Add(placement);
            if (!Buckets.TryGetValue((key, ""), out var bucket))
                Buckets[(key, "")] = bucket = [];
            bucket.Add(placement);
            return placement;
        }

        public WmoSceneViewerResult Locate() => _query.Locate(Objects, Buckets, Eye, ref TerrainLimit);

        public void Dispose()
        {
            foreach (var placement in _placements)
                WMOCache.Release(placement.FileDataId, 0);
            foreach (var key in _keys)
                _cache.Remove(key);
            DeviceField.SetValue(null, _previousDevice);
        }
    }
}
