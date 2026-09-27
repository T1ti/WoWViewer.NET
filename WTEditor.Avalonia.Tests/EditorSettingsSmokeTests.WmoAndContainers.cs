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
    public void WmoPortalVisibility_CullsInteriorButNeverExteriorGroupsOrTheirModrDoodads()
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
            boundingBox = new BoundingBox(new(-0.8f), new(0.8f)),
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
            boundingBox = new BoundingBox(new(10f), new(11f)),
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
            boundingBox = new BoundingBox(new(30f), new(31f)),
            portalLinks = [],
            doodadReferences = [3]
        };
        var exteriorLitWithoutInteriorFlag = new WorldModelGroupBatches
        {
            flags = 0x40,
            boundingBox = new BoundingBox(new(40f), new(41f)),
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
        CollectionAssert.AreEqual(new[] { true, false, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { false, true, true, true, true }, doodads);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x8,
            boundingBox = exterior.boundingBox,
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
        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { true, true, true, true, true }, doodads);

        // A missing 0x2000 interior bit defines an exterior group even when the
        // redundant 0x8 exterior bit is absent. It must seed portal traversal.
        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0,
            boundingBox = exterior.boundingBox,
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
        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { true, true, true, true, true }, doodads);
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
