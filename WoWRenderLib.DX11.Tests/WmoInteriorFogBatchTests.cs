using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class WmoInteriorFogBatchTests
{
    [TestMethod]
    public void MatchingVisibilityMasksKeepDistinctPlacementFog()
    {
        var firstFog = new Wrath335FogState(10f, 100f, 1f, 0xFF203040u);
        var secondFog = new Wrath335FogState(15f, 120f, 1f, 0xFF304050u);
        var batch = new WmoVisibilityBatch();
        batch.Begin([true, false], [true], [true, false], firstFog);

        Assert.IsTrue(batch.Matches([true, false], [true], [true, false], firstFog));
        Assert.IsFalse(batch.Matches([true, false], [true], [true, false], secondFog));
        Assert.IsFalse(batch.Matches([true, false], [true], [true, false], null));
        Assert.IsFalse(batch.Matches([true, false], [true], [false, false], firstFog));
    }

    [TestMethod]
    public void InteriorPortalPropagationClearsAtExteriorLightingBoundary()
    {
        static WmoPortal Portal(float height) => new()
        {
            Vertices =
            [
                new(0.3f, -0.2f, height), new(0.7f, -0.2f, height),
                new(0.7f, 0.2f, height), new(0.3f, 0.2f, height)
            ],
            Normal = Vector3.UnitZ,
            Distance = -height,
            Bounds = new BoundingBox(new(0.3f, -0.2f, height),
                new(0.7f, 0.2f, height))
        };

        static WorldModelGroupBatches Group(uint flags, WmoPortalLink[] links,
            bool viewerFloor = false) => new()
        {
            flags = flags,
            mogiFlags = flags,
            boundingBox = new BoundingBox(new(-1f), new(1f)),
            mogiBoundingBox = new BoundingBox(new(-1f), new(1f)),
            portalLinks = links,
            raycastVertices = viewerFloor
                ? [new(-1f, -1f, 0f), new(1f, -1f, 0f), new(0f, 1f, 0f)]
                : [],
            raycastIndices = viewerFloor ? [0, 1, 2] : [],
            doodadReferences = []
        };

        var wmo = new WorldModel
        {
            legacyLighting = true,
            portalGraphValid = true,
            portals = [Portal(0.1f), Portal(0.2f), Portal(0.3f)],
            groupBatches =
            [
                Group(0x2000,
                    [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 }],
                    viewerFloor: true),
                Group(0,
                    [new WmoPortalLink { PortalIndex = 1, TargetGroupIndex = 2, Side = 1 }]),
                Group(0x40,
                    [new WmoPortalLink { PortalIndex = 2, TargetGroupIndex = 3, Side = 1 }]),
                Group(0, [])
            ]
        };
        var scratch = new WmoPortalVisibilityScratch();
        bool[] visible = new bool[4];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(0f, 0f, 0.5f), [true, true, true, true],
            visible, [], scratch, out _));
        CollectionAssert.AreEqual(new[] { true, true, true, true }, visible);
        CollectionAssert.AreEqual(new[] { true, true, false, false },
            scratch.PropagatedGroups);

        Assert.IsFalse(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, 1, 0, scratch.PropagatedGroups[3]));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, 1, 0, scratch.PropagatedGroups[1]));
        Assert.IsFalse(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, WmoMaterialPolicy.TransitionBatchCategory, 0,
            scratch.PropagatedGroups[1]));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, WmoMaterialPolicy.TransitionBatchCategory, 1,
            scratch.PropagatedGroups[1]));
        Assert.IsFalse(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, 1, 0, propagatedFromInterior: true,
            rootFlags: 0, hasPrimaryVertexColors: false));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(
            true, 0, 0, 1, 0, propagatedFromInterior: true,
            rootFlags: 0x2, hasPrimaryVertexColors: false));

        // The client callback ORs the render-node bit when a second portal
        // reaches a group through an uninterrupted interior route.
        wmo.portals = [.. wmo.portals, Portal(0.15f)];
        wmo.groupBatches[0] = Group(0x2000,
            [
                new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 },
                new WmoPortalLink { PortalIndex = 3, TargetGroupIndex = 3, Side = 1 }
            ], viewerFloor: true);
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(0f, 0f, 0.5f), [true, true, true, true],
            visible, [], scratch, out _));
        Assert.IsTrue(scratch.PropagatedGroups[3]);

        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(3f, 3f, 0.5f), [true, true, true, true],
            visible, [], scratch, out _));
        CollectionAssert.AreEqual(new[] { false, false, false, false },
            scratch.PropagatedGroups);
    }

    [TestMethod]
    public void ViewerGroupCanBeResolvedWithoutPortalGraph()
    {
        static WorldModelGroupBatches Floor(uint flags, float height) => new()
        {
            flags = flags,
            boundingBox = new BoundingBox(new(-1f), new(1f)),
            raycastVertices =
            [
                new(-1f, -1f, height), new(1f, -1f, height),
                new(0f, 1f, height)
            ],
            raycastIndices = [0, 1, 2]
        };

        var wmo = new WorldModel
        {
            groupBatches = [Floor(0x2000, -0.4f), Floor(0x2000, -0.1f), Floor(0x8, -0.6f)]
        };
        var scratch = new WmoPortalVisibilityScratch();

        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(
            wmo, Matrix4x4.Identity, Vector3.Zero,
            [true, true, true], scratch, out var groupIndex));
        Assert.AreEqual(1, groupIndex);
        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(
            wmo, Matrix4x4.Identity, Vector3.Zero,
            [false, false, true], scratch, out _));
    }

    [TestMethod]
    public void ViewerGroupRayCanReachAGroupBelowTheCameraBounds()
    {
        var wmo = new WorldModel
        {
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = new BoundingBox(
                        new Vector3(-1f, -1f, -1f),
                        new Vector3(1f, 1f, 0f)),
                    raycastVertices =
                    [
                        new(-1f, -1f, -0.5f), new(1f, -1f, -0.5f),
                        new(0f, 1f, -0.5f)
                    ],
                    raycastIndices = [0, 1, 2]
                }
            ]
        };

        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(
            wmo, Matrix4x4.Identity, new Vector3(0f, 0f, 1f),
            [true], new WmoPortalVisibilityScratch(), out var groupIndex));
        Assert.AreEqual(0, groupIndex);
    }

    [TestMethod]
    public void TerrainHitBeforeInteriorFloorKeepsViewerOutdoors()
    {
        var bounds = new BoundingBox(new(-1f, -1f, -1f),
            new(1f, 1f, 1f));
        var wmo = new WorldModel
        {
            legacyLighting = true,
            portalGraphValid = true,
            portals = [],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    flags = 0x2000,
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    raycastVertices =
                    [
                        new(-1f, -1f, 0f), new(1f, -1f, 0f),
                        new(0f, 1f, 0f)
                    ],
                    raycastIndices = [0, 1, 2],
                    portalLinks = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = []
                }
            ]
        };
        var eye = new Vector3(0f, 0f, 1f);
        var scratch = new WmoPortalVisibilityScratch();

        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true, true], scratch, out var group));
        Assert.AreEqual(0, group);
        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true, true], scratch, out _,
            maximumViewerDistance: 0.25f));

        bool[] visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity, eye, [true, true],
            visible, [], scratch, out _));
        CollectionAssert.AreEqual(new[] { true, false }, visible);
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity, eye, [true, true],
            visible, [], scratch, out _, maximumViewerDistance: 0.25f));
        CollectionAssert.AreEqual(new[] { false, true }, visible);
    }

    [TestMethod]
    public void InteriorBoundsWithoutTrianglesCannotEnableViewerFog()
    {
        var wmo = new WorldModel
        {
            legacyLighting = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = new BoundingBox(new(-1f), new(1f))
                }
            ]
        };

        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, Vector3.Zero, [true],
            new WmoPortalVisibilityScratch(), out _));
    }

    [TestMethod]
    public void LegacyViewerRayRequiresMogiGroupBounds()
    {
        var wmo = new WorldModel
        {
            legacyLighting = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = new BoundingBox(new(-1f), new(1f)),
                    mogiBoundingBox = new BoundingBox(
                        new(2f, 2f, -1f), new(3f, 3f, 1f)),
                    raycastVertices =
                    [
                        new(-1f, -1f, 0f), new(1f, -1f, 0f),
                        new(0f, 1f, 0f)
                    ],
                    raycastIndices = [0, 1, 2]
                }
            ]
        };
        var eye = new Vector3(0f, 0f, 1f);

        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true],
            new WmoPortalVisibilityScratch(), out _));

        wmo.legacyLighting = false;
        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true],
            new WmoPortalVisibilityScratch(), out _));
    }

    [TestMethod]
    public void LegacyViewerRayAcceptsClientFaceEdgeTolerance()
    {
        var bounds = new BoundingBox(new(-1f), new(1f));
        var wmo = new WorldModel
        {
            legacyLighting = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    raycastVertices =
                    [
                        new(-1f, -1f, 0f), new(1f, -1f, 0f),
                        new(-1f, 1f, 0f)
                    ],
                    raycastIndices = [0, 1, 2]
                }
            ]
        };
        var eye = new Vector3(0f, 0.001f, 1f);

        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true],
            new WmoPortalVisibilityScratch(), out _));
        wmo.legacyLighting = false;
        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true],
            new WmoPortalVisibilityScratch(), out _));
    }

    [TestMethod]
    public void NearSideExteriorPortalOverridesInteriorFloor()
    {
        var bounds = new BoundingBox(new(-1f), new(1f));
        var portal = new WmoPortal
        {
            Vertices =
            [
                new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
                new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)
            ],
            Normal = Vector3.UnitZ,
            Distance = -0.5f,
            Bounds = new BoundingBox(
                new(-0.5f, -0.5f, 0.5f),
                new(0.5f, 0.5f, 0.5f))
        };
        var wmo = new WorldModel
        {
            legacyLighting = true,
            portalGraphValid = true,
            portals = [portal],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    raycastVertices =
                    [
                        new(-1f, -1f, 0f), new(1f, -1f, 0f),
                        new(0f, 1f, 0f)
                    ],
                    raycastIndices = [0, 1, 2],
                    portalLinks =
                    [
                        new WmoPortalLink
                        {
                            PortalIndex = 0,
                            TargetGroupIndex = 1,
                            Side = -1
                        }
                    ]
                },
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = []
                }
            ]
        };
        var eye = new Vector3(0f, 0f, 1f);
        var scratch = new WmoPortalVisibilityScratch();

        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true, true], scratch, out _));
        Assert.IsTrue(scratch.PortalViewerOverride);
        Assert.AreEqual(-1, scratch.PrimaryViewerGroupIndex);

        bool[] visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity, eye, [true, true],
            visible, [], scratch, out _));
        CollectionAssert.AreEqual(new[] { false, true }, visible);
        CollectionAssert.AreEqual(new[] { false, false },
            scratch.PropagatedGroups);

        wmo.groupBatches[1] = new WorldModelGroupBatches
        {
            boundingBox = bounds,
            mogiBoundingBox = bounds,
            portalLinks = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, eye, [true, true], scratch, out var nearGroup));
        Assert.AreEqual(1, nearGroup);
        Assert.AreEqual(0, scratch.SecondaryViewerGroupIndex);

        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, new Vector3(0f, 0f, 0.55f),
            [true, true], scratch, out _));
        Assert.AreEqual(0f, scratch.PrimaryViewerHitDistance);
    }

    [TestMethod]
    public void EmptyLegacyPortalGraphStillCullsInteriorGroupsFromOutdoors()
    {
        var empty = new PreppedWMO
        {
            LegacyLighting = true,
            PreppedWMOGroups = [new PreppedWMOGroup(), new PreppedWMOGroup()],
            PortalReferences = []
        };
        Assert.IsTrue(WMOLoader.ValidatePortalGraph(empty, [0, 1], []));
        Assert.IsTrue(WMOLoader.ValidatePortalGraph(empty, [0, 1],
            [new WmoPortal()])); // An unused root portal has no group links.

        var modern = new PreppedWMO
        {
            LegacyLighting = false,
            PreppedWMOGroups = empty.PreppedWMOGroups,
            PortalReferences = []
        };
        Assert.IsFalse(WMOLoader.ValidatePortalGraph(modern, [0, 1], []));

        var bounds = new BoundingBox(new Vector3(-0.5f, -0.5f, 0.2f),
            new Vector3(0.5f, 0.5f, 0.8f));
        var wmo = new WorldModel
        {
            legacyLighting = true,
            portalGraphValid = true,
            portals = [],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = []
                }
            ]
        };
        bool[] visibleGroups = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(0f, 0f, -1f), [true, true],
            visibleGroups, [], new WmoPortalVisibilityScratch(), out _));
        CollectionAssert.AreEqual(new[] { false, true }, visibleGroups);
    }

    [TestMethod]
    public void ExteriorFloorHitOverridesInteriorBoundsOnlyViewerCandidate()
    {
        var bounds = new BoundingBox(new Vector3(-1f, -1f, -1f),
            new Vector3(1f, 1f, 0.5f));
        var wmo = new WorldModel
        {
            legacyLighting = true,
            portalGraphValid = true,
            portals = [],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = []
                },
                new WorldModelGroupBatches
                {
                    flags = 0x8,
                    mogiFlags = 0x8,
                    boundingBox = bounds,
                    mogiBoundingBox = bounds,
                    portalLinks = [],
                    raycastVertices =
                    [
                        new(-1f, -1f, 0.2f), new(1f, -1f, 0.2f),
                        new(0f, 1f, 0.2f)
                    ],
                    raycastIndices = [0, 1, 2]
                }
            ]
        };
        bool[] visibleGroups = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo,
            Matrix4x4.Identity, Matrix4x4.Identity,
            new Vector3(0f, 0f, 0.5f), [true, true],
            visibleGroups, [], new WmoPortalVisibilityScratch(), out _));
        CollectionAssert.AreEqual(new[] { false, true }, visibleGroups);
    }
}
