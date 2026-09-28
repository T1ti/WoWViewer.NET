using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void WmoPortalVisibility_UsesClientExteriorSeedsAndPortalSideForGroupDoodads()
    {
        var portal = new WmoPortal
        {
            Vertices =
            [
                new(-0.25f, -0.25f, 0f),
                new(0.25f, -0.25f, 0f),
                new(0.25f, 0.25f, 0f),
                new(-0.25f, 0.25f, 0f)
            ],
            Normal = Vector3.UnitZ,
            Distance = 0f,
            Bounds = new BoundingBox(new(-0.25f, -0.25f, 0f), new(0.25f, 0.25f, 0f))
        };
        var exterior = new WorldModelGroupBatches
        {
            flags = 0x8,
            mogiFlags = 0x8,
            boundingBox = new BoundingBox(new(-0.8f), new(0.8f)),
            mogiBoundingBox = new BoundingBox(new(-0.8f), new(0.8f)),
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 }],
            doodadReferences = []
        };
        var interior = new WorldModelGroupBatches
        {
            flags = 0x2000,
            boundingBox = new BoundingBox(new(-0.4f), new(0.4f)),
            portalLinks = [],
            doodadReferences = [0]
        };
        var disconnectedExterior = new WorldModelGroupBatches
        {
            flags = 0x8,
            mogiFlags = 0x8,
            boundingBox = new BoundingBox(new(10f), new(11f)),
            mogiBoundingBox = new BoundingBox(new(10f), new(11f)),
            portalLinks = [],
            doodadReferences = [1]
        };
        var unclassifiedOutdoor = new WorldModelGroupBatches
        {
            flags = 0,
            boundingBox = new BoundingBox(new(20f), new(21f)),
            portalLinks = [],
            doodadReferences = [2]
        };
        var ambiguousOutdoor = new WorldModelGroupBatches
        {
            flags = 0x8 | 0x2000,
            mogiFlags = 0x8 | 0x2000,
            boundingBox = new BoundingBox(new(30f), new(31f)),
            mogiBoundingBox = new BoundingBox(new(30f), new(31f)),
            portalLinks = [],
            doodadReferences = [3]
        };
        var exteriorLitWithoutInteriorFlag = new WorldModelGroupBatches
        {
            flags = 0x40,
            mogiFlags = 0x40,
            boundingBox = new BoundingBox(new(40f), new(41f)),
            mogiBoundingBox = new BoundingBox(new(40f), new(41f)),
            portalLinks = [],
            doodadReferences = [4]
        };
        var wmo = new WorldModel
        {
            groupBatches =
            [
                exterior,
                interior,
                disconnectedExterior,
                unclassifiedOutdoor,
                ambiguousOutdoor,
                exteriorLitWithoutInteriorFlag
            ],
            portals = [portal],
            portalGraphValid = true,
            doodads = new WMODoodad[5],
            doodadsReferencedByGroups = [true, true, true, true, true]
        };
        var groups = new bool[6];
        var doodads = new bool[5];
        var scratch = new WmoPortalVisibilityScratch();

        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, false, false, false, false, false }, groups);
        CollectionAssert.AreEqual(new[] { false, false, false, false, false }, doodads);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x8,
            mogiFlags = 0x8,
            boundingBox = exterior.boundingBox,
            mogiBoundingBox = exterior.mogiBoundingBox,
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, true, false, false, false, false }, groups);
        CollectionAssert.AreEqual(new[] { true, false, false, false, false }, doodads);

        // The client's interior viewer-group test excludes 0x48 exterior flags;
        // it does not require the 0x2000 flag to traverse from a containing room.
        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0,
            boundingBox = exterior.boundingBox,
            mogiBoundingBox = exterior.mogiBoundingBox,
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, true, false, false, false, false }, groups);
        CollectionAssert.AreEqual(new[] { true, false, false, false, false }, doodads);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x40,
            mogiFlags = 0x40,
            boundingBox = exterior.boundingBox,
            mogiBoundingBox = exterior.mogiBoundingBox,
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo, Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f), new[] { true, true, true, true, true, true },
            groups, doodads, scratch, out _));
        CollectionAssert.AreEqual(new[] { true, true, false, false, false, false }, groups);
    }

    [TestMethod]
    public void WmoPortalVisibility_ClipsNestedPortalsAndCullBatchesWithinVisibleGroup()
    {
        static WmoPortal Portal(float minX, float maxX, float z) => new()
        {
            Vertices =
            [
                new(minX, -0.2f, z), new(maxX, -0.2f, z),
                new(maxX, 0.2f, z), new(minX, 0.2f, z)
            ],
            Normal = Vector3.UnitZ,
            Distance = -z,
            Bounds = new BoundingBox(new(minX, -0.2f, z), new(maxX, 0.2f, z))
        };

        var wmo = new WorldModel
        {
            portalGraphValid = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = new BoundingBox(new(-1f), new(1f)),
                    mogiBoundingBox = new BoundingBox(new(-1f), new(1f)),
                    portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
                    doodadReferences = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x2000,
                    boundingBox = new BoundingBox(new(-1f, -1f, 0f), new(1f)),
                    portalLinks = [new WmoPortalLink { PortalIndex = 1, TargetGroupIndex = 2, Side = -1 }],
                    doodadReferences = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x2000,
                    boundingBox = new BoundingBox(new(0.6f, -0.2f, 0.4f), new(0.9f, 0.2f, 0.8f)),
                    portalLinks = [],
                    doodadReferences = []
                }
            ],
            portals = [Portal(-0.25f, 0.25f, 0f), Portal(0.6f, 0.9f, 0.4f)],
            wmoRenderBatches =
            [
                new WMORenderBatch { groupID = 0, hasBounds = true,
                    bounds = new BoundingBox(new(-0.5f, -0.5f, 0f), new(0.5f)) },
                new WMORenderBatch { groupID = 1, hasBounds = true,
                    bounds = new BoundingBox(new(-0.1f, -0.1f, 0.2f), new(0.1f, 0.1f, 0.4f)) },
                new WMORenderBatch { groupID = 1, hasBounds = true,
                    bounds = new BoundingBox(new(0.6f, -0.1f, 0.2f), new(0.8f, 0.1f, 0.4f)) },
                new WMORenderBatch { groupID = 1, hasBounds = true,
                    bounds = new BoundingBox(new(-0.1f, 0.6f, 0.2f), new(0.1f, 0.8f, 0.4f)) },
                new WMORenderBatch { groupID = 2, hasBounds = true,
                    bounds = new BoundingBox(new(0.6f, -0.1f, 0.5f), new(0.8f, 0.1f, 0.7f)) }
            ],
            firstRenderBatchByGroup = [0, 1, 4],
            renderBatchCountByGroup = [1, 3, 1],
            doodads = [],
            doodadsReferencedByGroups = []
        };
        var groups = new bool[3];
        var batches = new bool[5];

        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, new Vector3(0f, 0f, -0.5f),
            [true, true, true], groups, Span<bool>.Empty, batches,
            new WmoPortalVisibilityScratch(), out _));
        CollectionAssert.AreEqual(new[] { true, true, false }, groups);
        CollectionAssert.AreEqual(new[] { true, true, false, false, false }, batches);
    }

    [TestMethod]
    public void WmoPortalVisibility_ClosestViewerFloorSelectsExteriorOverOverlappingRoomBounds()
    {
        static WorldModelGroupBatches Floor(uint flags, float height) => new()
        {
            flags = flags,
            mogiFlags = flags,
            boundingBox = new BoundingBox(new(-1f), new(1f)),
            mogiBoundingBox = new BoundingBox(new(-1f), new(1f)),
            raycastVertices =
            [
                new(-1f, -1f, height), new(1f, -1f, height),
                new(0f, 1f, height)
            ],
            raycastIndices = [0, 1, 2],
            portalLinks = [],
            doodadReferences = []
        };

        var wmo = new WorldModel
        {
            portalGraphValid = true,
            groupBatches = [Floor(0x2000, -0.4f), Floor(0x8, -0.1f)],
            portals = [],
            doodads = [],
            doodadsReferencedByGroups = []
        };
        var groups = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, Vector3.Zero, [true, true], groups,
            Span<bool>.Empty, new WmoPortalVisibilityScratch(), out _));
        CollectionAssert.AreEqual(new[] { false, true }, groups);
    }

    [TestMethod]
    public void WmoPortalVisibility_DoesNotTraversePortalBehindCamera()
    {
        var portal = new WmoPortal
        {
            Vertices =
            [
                new(-0.2f, -0.2f, 5f), new(0.2f, -0.2f, 5f),
                new(0.2f, 0.2f, 5f), new(-0.2f, 0.2f, 5f)
            ],
            Normal = Vector3.UnitZ,
            Distance = -5f,
            Bounds = new BoundingBox(new(-0.2f, -0.2f, 5f), new(0.2f, 0.2f, 5f))
        };
        var wmo = new WorldModel
        {
            portalGraphValid = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = new BoundingBox(new(-2f, -2f, -5f), new(2f, 2f, 5f)),
                    mogiBoundingBox = new BoundingBox(new(-2f, -2f, -5f), new(2f, 2f, 5f)),
                    portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
                    doodadReferences = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x2000,
                    boundingBox = new BoundingBox(new(-1f, -1f, 4f), new(1f, 1f, 6f)),
                    portalLinks = [],
                    doodadReferences = []
                }
            ],
            portals = [portal],
            doodads = [],
            doodadsReferencedByGroups = []
        };
        var groups = new bool[2];
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2f, 1f, 0.1f, 100f);
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            projection, Vector3.Zero, [true, true], groups,
            Span<bool>.Empty, new WmoPortalVisibilityScratch(), out _));
        CollectionAssert.AreEqual(new[] { true, false }, groups);
    }

    [TestMethod]
    public void WmoPortalVisibility_UsesRootGroupFlagsAndBoundsForOutdoorSeeds()
    {
        var near = new BoundingBox(new(-1f, -1f, 0.2f), new(1f));
        var far = new BoundingBox(new(10f), new(11f));
        var wmo = new WorldModel
        {
            portalGraphValid = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0,
                    boundingBox = near,
                    mogiBoundingBox = near,
                    portalLinks = [],
                    doodadReferences = []
                }
            ],
            portals = [],
            doodads = [],
            doodadsReferencedByGroups = []
        };
        var groups = new bool[1];
        var scratch = new WmoPortalVisibilityScratch();
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, Vector3.Zero, [true], groups,
            Span<bool>.Empty, scratch, out _));
        Assert.IsFalse(groups[0]);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x2000,
            mogiFlags = 0x8,
            boundingBox = near,
            mogiBoundingBox = far,
            portalLinks = [],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, Vector3.Zero, [true], groups,
            Span<bool>.Empty, scratch, out _));
        Assert.IsFalse(groups[0]);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x2000,
            mogiFlags = 0x8,
            boundingBox = near,
            mogiBoundingBox = near,
            portalLinks = [],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, Vector3.Zero, [true], groups,
            Span<bool>.Empty, scratch, out _));
        Assert.IsTrue(groups[0]);
    }

    private static ADTRenderBatch CreateTerrainBatch(int material, float scale) => new()
    {
        layerCount = 1,
        materialFDIDs = [material, -1, -1, -1, -1, -1, -1, -1],
        heightMaterialFDIDs = [material, -1, -1, -1, -1, -1, -1, -1],
        scales = [scale, 1, 1, 1, 1, 1, 1, 1],
        heightScales = [1, 1, 1, 1, 1, 1, 1, 1],
        heightOffsets = [0, 0, 0, 0, 0, 0, 0, 0]
    };

    private static FrameProfileSnapshot CreateBenchmarkSnapshot(
        DateTimeOffset capturedAt,
        int pendingAssets = 0,
        int drawCalls = 100,
        bool hasWorld = true) =>
        new(
            1,
            capturedAt,
            16,
            5,
            4,
            Array.Empty<FrameTimingStep>(),
            drawCalls,
            1_000,
            pendingAssets)
        {
            Culling = new CullingMetrics(
                hasWorld ? 10 : 0,
                hasWorld ? 10 : 0,
                0,
                0,
                0,
                0)
        };

    [TestMethod]
    public void ContainerTransformChange_InvalidatesMatrixAndWorldBounds()
    {
        var container = new Container3D(default, 1, 1)
        {
            Position = new Vector3(10, 20, 30),
            Rotation = Vector3.Zero,
            Scale = 1
        };
        var firstMatrix = container.GetModelMatrix();
        container.CachedBoundingSphere = new BoundingSphere(Vector3.Zero, 1);
        container.CachedBoundingBox = new BoundingBox(Vector3.Zero, Vector3.One);

        container.Position = new Vector3(11, 20, 30);

        Assert.IsNull(container.ModelMatrix);
        Assert.IsNull(container.CachedBoundingSphere);
        Assert.IsNull(container.CachedBoundingBox);
        Assert.AreNotEqual(firstMatrix, container.GetModelMatrix());
    }

}
