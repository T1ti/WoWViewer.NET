using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoViewerQueryTests
{
    private static readonly BoundingBox Bounds = new(new(-10f, -10f, -2000f), new(10f, 10f, 10f));

    [TestMethod]
    public void EqualGeometryHitsReplaceThePreviousGroupAndExteriorLightingStaysInside()
    {
        var wmo = Model(Floor(0f), Floor(0f, 0x40));
        Assert.IsTrue(Locate(wmo, out var group, out _));
        Assert.AreEqual(1, group);
        wmo.groupBatches[1] = Floor(0f, 8);
        Assert.IsFalse(Locate(wmo, out _, out var scratch));
        Assert.AreEqual(1f, scratch.PrimaryViewerHitDistance);
    }

    [TestMethod]
    public void GeometryUsesAnInclusiveExactLimitWithoutPortalTieTolerance()
    {
        var wmo = Model(Floor(0f), Floor(-0.00005f));
        Assert.IsTrue(Locate(wmo, out var group, out _, maximum: 1f));
        Assert.AreEqual(0, group);
        Assert.IsFalse(Locate(Model(Floor(-0.00005f)), out _, out _, maximum: 1f));
        Assert.IsTrue(Locate(Model(Floor(0f)), out _, out _, maximum: 1f));
    }

    [TestMethod]
    public void PortalOverrideUsesStrictNormalizedFractionAndIgnoresTheNarrowedBroadPhase()
    {
        Assert.IsTrue(Wrath335WmoViewerQuery.PortalCanOverride(0.000099f, 0f));
        Assert.IsFalse(Wrath335WmoViewerQuery.PortalCanOverride(0.0001f, 0f));
        var wmo = WithPortal(-0.15f);
        Assert.IsTrue(Locate(wmo, out var group, out var scratch));
        Assert.AreEqual(1, group);
        Assert.IsTrue(scratch.PortalViewerOverride);
        Assert.AreEqual(1.15f, scratch.PrimaryViewerHitDistance, 0.000001f);
        wmo = WithPortal(-0.2f);
        Assert.IsTrue(Locate(wmo, out group, out scratch));
        Assert.AreEqual(0, group);
        Assert.IsFalse(scratch.PortalViewerOverride);
    }

    [TestMethod]
    public void PortalFractionIsIndependentOfPlacementScaleAndExteriorNearSideDiscardsFarSide()
    {
        var wmo = WithPortal(-0.075f);
        Assert.IsTrue(Locate(wmo, out var group, out _, Matrix4x4.CreateScale(2f)));
        Assert.AreEqual(1, group);
        wmo.groupBatches[1] = Floor(-0.075f, 8);
        Assert.IsFalse(Locate(wmo, out _, out var scratch, Matrix4x4.CreateScale(2f)));
        Assert.AreEqual(-1, scratch.SecondaryViewerGroupIndex);
        Assert.IsTrue(scratch.PortalViewerOverride);
    }

    [TestMethod]
    public void PolygonProjectionUsesClientMajorAxisTiesAndAsymmetricBoundaryRules()
    {
        Assert.AreEqual(2, Wrath335WmoViewerQuery.MajorAxis(Vector3.One));
        Assert.AreEqual(1, Wrath335WmoViewerQuery.MajorAxis(new(1f, 1f, 0f)));
        Vector3[] square = [new(-1f, -1f, 0f), new(1f, -1f, 0f),
            new(1f, 1f, 0f), new(-1f, 1f, 0f)];
        Assert.IsTrue(Wrath335WmoViewerQuery.PointInPortal(Vector3.Zero, square, Vector3.UnitZ));
        Assert.IsTrue(Wrath335WmoViewerQuery.PointInPortal(new(0f, 1f, 0f), square, Vector3.UnitZ));
        Assert.IsFalse(Wrath335WmoViewerQuery.PointInPortal(new(0f, -1f, 0f), square, Vector3.UnitZ));
        Assert.IsFalse(Wrath335WmoViewerQuery.PointInPortal(new(1.001f, 0f, 0f), square, Vector3.UnitZ));
    }

    [TestMethod]
    public void ExactViewerSeedsOneGeometryWinnerAndModernSelectionRemainsUnchanged()
    {
        var wmo = Model(Floor(0f), Floor(0f));
        wmo.portalGraphValid = true;
        bool[] visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(wmo, Matrix4x4.Identity,
            Matrix4x4.Identity, new(0f, 0f, 1f), [true, true], visible, [],
            new(), out _));
        CollectionAssert.AreEqual(new[] { false, true }, visible);
        wmo.wrath335 = false;
        Assert.IsTrue(Locate(wmo, out var group, out _));
        Assert.AreEqual(0, group);
    }

    private static bool Locate(WorldModel wmo, out int group,
        out WmoPortalVisibilityScratch scratch, Matrix4x4? model = null, float maximum = 1760f)
    {
        scratch = new();
        return WmoPortalVisibility.TryLocateViewerGroup(wmo, model ?? Matrix4x4.Identity,
            new(0f, 0f, 1f), Enumerable.Repeat(true, wmo.groupBatches.Length).ToArray(),
            scratch, out group, maximum);
    }

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        legacyLighting = true, wrath335 = true, boundingBox = Bounds,
        groupBatches = groups, portals = [], doodads = []
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
        var owner = Floor(0f);
        owner = owner with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }]
        };
        var wmo = Model(owner, Floor(-5f));
        wmo.portals = [new()
        {
            Normal = Vector3.UnitZ, Distance = -height,
            Vertices = [new(-1f, -1f, height), new(1f, -1f, height),
                new(1f, 1f, height), new(-1f, 1f, height)]
        }];
        return wmo;
    }
}
