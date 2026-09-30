using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalTraversalTests
{
    [TestMethod]
    public void OneOwnedReferenceTraversesOnlyFromItsOwnerAndRespectsItsSide()
    {
        var model = Model(Group(0), Group(0));
        model.groupBatches[0] = model.groupBatches[0] with { portalLinks = [Link(1)] };
        CollectionAssert.AreEqual(new[] { true, true }, Compute(model, new(0f, 0f, 1f), new(0, -1), out _));
        CollectionAssert.AreEqual(new[] { false, true }, Compute(model, new(0f, 0f, 1f), new(1, -1), out _));
        CollectionAssert.AreEqual(new[] { true, false }, Compute(model, new(0f, 0f, 0f), new(0, -1), out _));
    }

    [TestMethod]
    public void CameraOnIncludedPortalEdgeKeepsTheFullViewForTheLinkedRoom()
    {
        var model = Model(Group(0), Group(0));
        model.groupBatches[0] = model.groupBatches[0] with { portalLinks = [Link(1)] };
        model.groupBatches[1] = model.groupBatches[1] with { portalLinks = [Link(0, -1)] };
        var scratch = new WmoPortalVisibilityScratch();
        var projection = Matrix4x4.CreateLookAt(new(0f, 1f, 0.5f), new(0f, 2f, 0.5f), Vector3.UnitZ) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.2f, 10f);
        var visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, projection,
            new(0f, 1f, 0.5f), [true, true], visible, [], [], scratch, out _, 1760f, new(0, -1)));
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        Assert.AreEqual(WmoPortalRect.Full, scratch.PortalRects[0]);
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, projection,
            new(0f, 1f, 0.5f), [true, true], visible, [], [], scratch, out _, 1760f, new(1, 0)));
        CollectionAssert.AreEqual(new[] { true, true }, visible);
    }

    [TestMethod]
    public void NativeDepthProcessesLevelTenPortalsButDoesNotMarkLevelEleven()
    {
        var model = Model(Enumerable.Range(0, 12).Select(_ => Group(0)).ToArray());
        for (ushort i = 0; i < 11; i++)
            model.groupBatches[i] = model.groupBatches[i] with { portalLinks = [Link((ushort)(i + 1))] };
        var visible = Compute(model, new(0f, 0f, 1f), new(0, -1), out var traversed);
        Assert.AreEqual(11, traversed);
        Assert.IsTrue(visible.Take(11).All(v => v));
        Assert.IsFalse(visible[11]);
    }

    [TestMethod]
    public void CyclesRevisitEarlierGroupsUntilDepthLimitAndOnlySkipImmediateBackEdges()
    {
        var model = Model(Group(0), Group(0), Group(0));
        for (ushort i = 0; i < 3; i++)
            model.groupBatches[i] = model.groupBatches[i] with { portalLinks = [Link((ushort)((i + 1) % 3))] };
        CollectionAssert.AreEqual(new[] { true, true, true }, Compute(model, new(0f, 0f, 1f), new(0, -1), out var traversed));
        Assert.AreEqual(11, traversed);
        model.groupBatches[1] = model.groupBatches[1] with { portalLinks = [Link(0)] };
        Compute(model, new(0f, 0f, 1f), new(0, -1), out traversed);
        Assert.AreEqual(2, traversed);
    }

    [TestMethod]
    public void LoadedAlwaysDrawFlagReturnsBeforeCallbackAndExteriorPassStopsAtMogiExterior()
    {
        var model = Model(Group(0), Group(0x10000) with { mogiFlags = 0 });
        model.groupBatches[0] = model.groupBatches[0] with { portalLinks = [Link(1)] };
        CollectionAssert.AreEqual(new[] { true, false }, Compute(model, new(0f, 0f, 1f), new(0, -1), out _));
        model = Model(Group(0) with { mogiFlags = 8 }, Group(0) with { mogiFlags = 8,
            mogiBoundingBox = new(new(5f), new(6f)) });
        model.groupBatches[0] = model.groupBatches[0] with { portalLinks = [Link(1)] };
        CollectionAssert.AreEqual(new[] { true, false }, Compute(model, new(0f, 0f, 1f), new(-1, -1), out _));
    }

    [TestMethod]
    public void NativeIntersectionPreservesMaxYQuirkAndAcceptsExactDegeneracyThreshold()
    {
        var parent = new WmoPortalRect(-0.2f, -0.2f, 0.2f, 0.2f);
        Assert.IsTrue(parent.TryIntersectWrath335(new(-0.1f, -0.1f, 0.8f, 0.8f), out var intersection));
        Assert.AreEqual(new WmoPortalRect(-0.1f, -0.1f, 0.2f, 0.8f), intersection);
        Assert.IsTrue(WmoPortalRect.Full.TryIntersectWrath335(new(0f, 0f, 0.001f, 0.001f), out _));
        Assert.IsFalse(WmoPortalRect.Full.TryIntersectWrath335(new(0f, 0f, 0.000999f, 0.001f), out _));
    }

    [TestMethod]
    public void NullAndUnownedReferencesDoNotInvalidateTheWrathGraph()
    {
        var source = new PreppedWMO { Wrath335 = true, LegacyLighting = true,
            PreppedWMOGroups = [new() { portalStart = 0, portalCount = 1 }],
            PortalReferences = [new() { PortalIndex = ushort.MaxValue, GroupIndex = ushort.MaxValue },
                new() { PortalIndex = ushort.MaxValue, GroupIndex = 999 }] };
        Assert.IsTrue(WMOLoader.ValidatePortalGraph(source, [0], []));
        Assert.AreEqual(0, WMOLoader.BuildPortalLinks(source.PreppedWMOGroups[0], source.PortalReferences, [0]).Length);
        var malformed = source with { PreppedWMOGroups = [new() { portalStart = 1, portalCount = 1 }] };
        Assert.IsFalse(WMOLoader.ValidatePortalGraph(malformed, [0], []));
        var otherClient = source with { Wrath335 = false };
        Assert.IsFalse(WMOLoader.ValidatePortalGraph(otherClient, [0], []));
    }

    [TestMethod]
    public void WrathPortalPlaneRetainsTheOriginalNormalAndDistance()
    {
        var source = new PreppedWMO { Wrath335 = true, LegacyLighting = true,
            PortalVertices = Wrath335PortalProjectionTests.Square(0.5f).Vertices,
            Portals = [new() { VertexCount = 4, Normal = new(0f, 0f, 0.99999994f), Distance = -0.5f }] };
        var portal = WMOLoader.BuildPortals(source)[0];
        Assert.AreEqual(source.Portals[0].Normal, portal.Normal);
        Assert.AreEqual(source.Portals[0].Distance, portal.Distance);
        Assert.AreEqual(Vector3.UnitZ, WMOLoader.BuildPortals(source with { Wrath335 = false })[0].Normal);
    }

    [TestMethod]
    public void OwnedRangeAtTheUshortBoundaryDoesNotWrapItsIterationIndex()
    {
        var references = new PreppedWMOPortalReference[65536];
        references[65535] = new() { PortalIndex = 17, GroupIndex = 0, Side = -1 };
        var links = WMOLoader.BuildPortalLinks(new() { portalStart = 65535, portalCount = 1 }, references, [0]);
        Assert.AreEqual(1, links.Length);
        Assert.AreEqual((ushort)17, links[0].PortalIndex);
        Assert.AreEqual((short)-1, links[0].Side);
    }

    private static bool[] Compute(WorldModel model, Vector3 eye, WmoViewerGroups seeds, out int traversed)
    {
        var visible = new bool[model.groupBatches.Length];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, Matrix4x4.Identity,
            eye, Enumerable.Repeat(true, visible.Length).ToArray(), visible, [], [], new(), out traversed, 1760f, seeds));
        return visible;
    }

    private static WmoPortalLink Link(ushort target, short side = 1) => new()
        { PortalIndex = 0, TargetGroupIndex = target, Side = side };
    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, portalGraphValid = true,
        boundingBox = new(new(-10f), new(10f)), groupBatches = groups,
        portals = [Wrath335PortalProjectionTests.Square(0.5f)], doodads = [], wmoRenderBatches = []
    };
    private static WorldModelGroupBatches Group(uint flags) => new()
    {
        flags = flags, mogiFlags = flags, portalLinks = [], doodadReferences = [],
        boundingBox = new(new(-1f, -1f, 0.1f), new(1f, 1f, 0.8f)),
        mogiBoundingBox = new(new(-1f, -1f, 0.1f), new(1f, 1f, 0.8f))
    };
}
