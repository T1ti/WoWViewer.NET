using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335ExteriorPortalViewsTests
{
    [TestMethod]
    public void EmptyInteriorDestinationRetainsPolygonAndUnitRectWithoutChangingSkyUnions()
    {
        var model = Model(Group(8, Link(0, 1)), Group(0));
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(false);
        var scratch = Compute(model, out var visible, scene: scene);
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        var view = scratch.ExteriorPortalViews.Forwarded.Views[0];
        Assert.AreEqual(new WmoPortalRect(0.4f, 0.4f, 0.6f, 0.6f), view.Rect);
        Assert.AreEqual(4, view.VertexCount);
        Assert.AreEqual(1, scene.RenderViews.Views.Length);
        Assert.AreEqual(WmoPortalRect.Full, scene.SkyRect);
        Assert.AreEqual(WmoPortalRect.Full, scene.ExteriorRect);
        Assert.AreEqual(0f, scene.ExteriorDistance);
        var polygon = scene.RenderViews.Polygon(scene.RenderViews.Views[0]);
        CollectionAssert.AreEqual(new Vector3[] { new(-0.2f, -0.2f, -0.02f),
            new(0.2f, -0.2f, -0.02f), new(0.2f, 0.2f, -0.02f), new(-0.2f, 0.2f, -0.02f) },
            polygon.ToArray(), new Vector3ToleranceComparer());
    }

    [TestMethod]
    public void BackFacingDepthZeroLinkBlocksEarlierCandidateEvenWhenItsProjectionIsEmpty()
    {
        var model = Model(Group(8, Link(0, 1), Link(1, 2, -1)), Group(0), Group(0));
        model.portals = [Portal(), Portal(centerX: 5f)];
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true, false }, visible);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void RecursiveLinksNeitherEmitCandidatesNorAddDepthZeroBlockers()
    {
        var model = Model(Group(8, Link(0, 1)), Group(0, Link(1, 2), Link(2, 3, -1)),
            Group(0), Group(0));
        model.portals = [Portal(), Portal(0.1f), Portal(0.15f)];
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true, true, false }, visible);
        Assert.AreEqual(1, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void BlockersResetBetweenSeedsButBlockedEmissionStaysDeduplicated()
    {
        var model = Model(Group(8, Link(0, 2), Link(1, 3, -1)),
            Group(8, Link(0, 2), Link(2, 4)), Group(0), Group(0), Group(0));
        model.portals = [Portal(), Portal(), Portal(0.1f)];
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true, true, false, true }, visible);
        var views = scratch.ExteriorPortalViews.Forwarded.Views;
        Assert.AreEqual(1, views.Length);
        Assert.AreEqual(new WmoPortalRect(0.45f, 0.45f, 0.55f, 0.55f), views[0].Rect);
    }

    [TestMethod]
    public void SharedPortalEmitsOnceAcrossDestinationsAndExteriorSeeds()
    {
        var model = Model(Group(8, Link(0, 2), Link(0, 3)),
            Group(8, Link(0, 3)), Group(0), Group(0));
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true, true, true }, visible);
        Assert.AreEqual(1, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [DataTestMethod]
    [DataRow(0u, 1)]
    [DataRow(8u, 0)]
    [DataRow(0x10000u, 0)]
    [DataRow(0x40u, 0)]
    [DataRow(0x100u, 0)]
    [DataRow(0x40000u, 1)]
    public void ExteriorEmissionUsesRootDestinationMasksBeforeLoadedLookup(uint rootFlags, int count)
    {
        var model = Model(Group(8, Link(0, 1)), Group(0x140) with { mogiFlags = rootFlags });
        var scratch = Compute(model, out var visible, enabled: [true, false]);
        CollectionAssert.AreEqual(new[] { true, false }, visible);
        Assert.AreEqual(count, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void InteriorEmissionBitFourSuppressesLaterExteriorEmissionOfTheSamePortal()
    {
        var model = Model(Group(0, Link(0, 1)), Group(8, Link(0, 2)), Group(0));
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(true);
        var scratch = Compute(model, out var visible, scene: scene, seeds: new(0, -1));
        Assert.IsTrue(scene.HasExteriorView);
        CollectionAssert.AreEqual(new[] { true, true, true }, visible);
        Assert.AreEqual(0, scene.RenderViews.Views.Length);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
        // The ordinary visibility API must stamp bit 4 even without a scene
        // union sink, as used by secondary placements in the WMO render loop.
        // A registered exterior-lit viewer permits its exterior seed pass
        // without relying on the still-unported global exterior union handoff.
        model.groupBatches[0] = Group(0x40, Link(0, 1)) with { mogiFlags = 0 };
        scratch = Compute(model, out visible, seeds: new(0, -1));
        CollectionAssert.AreEqual(new[] { true, true, true }, visible);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void FailedOffsetProjectionKeepsEyeContainmentAndResetsEmissionOnTheNextFrame()
    {
        var model = Model(Group(8, Link(0, 2, -1)), Group(8, Link(0, 2, -1)), Group(0));
        model.portals = [Portal(z: 0.995f)];
        var scratch = Compute(model, out var visible, eye: new(0f, 0f, 0.99f));
        CollectionAssert.AreEqual(new[] { true, true, true }, visible);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
        var buffer = scratch.EmittedPortalViews;
        model.groupBatches[0] = Group(8, Link(0, 2));
        model.groupBatches[1] = Group(8, Link(0, 2));
        model.portals = [Portal()];
        Compute(model, out visible, scratch: scratch);
        Assert.AreSame(buffer, scratch.EmittedPortalViews);
        Assert.AreEqual(1, scratch.ExteriorPortalViews.Forwarded.Views.Length);
        model.groupBatches[0] = Group(8);
        model.groupBatches[1] = Group(8);
        Compute(model, out visible, scratch: scratch);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void NegativeSideOffsetIsIndependentOfEyeContainmentAndKeepsUndividedInternalDepth()
    {
        var model = Model(Group(8, Link(0, 1, -1)), Group(0));
        var projection = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 10f);
        var scratch = Compute(model, out _, eye: Vector3.Zero, projection: projection);
        var view = scratch.ExteriorPortalViews.Forwarded.Views[0];
        Assert.AreEqual((1f - 0.2f / 0.51f) * 0.5f, view.Rect.MinX, 0.000001f);
        // Native perspective Z at distance .51, near .1, far 10:
        // ((far+near)*distance - 2*far*near) / (far-near).
        Assert.AreEqual(0.31828284f,
            scratch.ExteriorPortalViews.Forwarded.Polygon(view)[0].Z, 0.000001f);
    }

    [TestMethod]
    public void UnitRectangleBlockerOverlapIncludesTouchingEdgesAndForwardingKeepsOrder()
    {
        var queues = new Wrath335ExteriorPortalViews();
        Vector3[] polygon = [new(2f, 0f, 0f), new(3f, 0f, 0f), new(2f, 1f, 0f)];
        queues.BeginSeed();
        queues.AddCandidate(new(1f, -0.5f, 2f, 0.5f), polygon); // touches unit max-X
        queues.AddCandidate(new(1.1f, -0.5f, 2f, 0.5f), polygon);
        queues.AddCandidate(new(-3f, -0.5f, -1.1f, 0.5f), polygon);
        queues.AddBlocker();
        queues.EndSeed();
        var views = queues.Forwarded.Views;
        Assert.AreEqual(2, views.Length);
        Assert.AreEqual(1.05f, views[0].Rect.MinX);
        Assert.AreEqual(-1f, views[1].Rect.MinX);
        polygon[0] = Vector3.Zero;
        Assert.AreEqual(new Vector3(2f, 0f, 0f), queues.Forwarded.Polygon(views[0])[0]);
        queues.BeginSeed();
        queues.AddCandidate(new(-0.2f, -0.2f, 0.2f, 0.2f), polygon);
        queues.EndSeed();
        Assert.AreEqual(3, queues.Forwarded.Views.Length);
    }

    [TestMethod]
    public void OtherClientProfilesDoNotProduceWrathRenderViews()
    {
        var model = Model(Group(8, Link(0, 1)), Group(0));
        model.wrath335 = false;
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [DataTestMethod]
    [DataRow(0.2f, true)]
    [DataRow(-0.2f, false)]
    public void CameraOnPortalEdgeEmitsOffsetPolygonInsteadOfEyeContainmentFullRect(float y, bool contained)
    {
        var model = Model(Group(8, Link(0, 1)), Group(0));
        var scratch = Compute(model, out var visible, eye: new(0f, y, 0.5f));
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        Assert.AreEqual(contained, scratch.PortalRects[0] == WmoPortalRect.Full);
        var view = scratch.ExteriorPortalViews.Forwarded.Views[0];
        Assert.AreEqual(new WmoPortalRect(0.4f, 0.4f, 0.6f, 0.6f), view.Rect);
        Assert.AreEqual(4, view.VertexCount);
    }

    private static WmoPortalVisibilityScratch Compute(WorldModel model, out bool[] visible,
        Wrath335PortalSceneViews? scene = null, WmoViewerGroups? seeds = null,
        bool[]? enabled = null, Vector3? eye = null, Matrix4x4? projection = null,
        WmoPortalVisibilityScratch? scratch = null)
    {
        scratch ??= new();
        visible = new bool[model.groupBatches.Length];
        enabled ??= Enumerable.Repeat(true, visible.Length).ToArray();
        if (scene == null)
            Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity,
                projection ?? Matrix4x4.Identity, eye ?? new(0f, 0f, 1f), enabled,
                visible, [], [], scratch, out _, 1760f, seeds ?? new(-1, -1)));
        else
            Assert.IsTrue(WmoPortalVisibility.TryComputeViewerScene(model, Matrix4x4.Identity,
                projection ?? Matrix4x4.Identity, eye ?? new(0f, 0f, 1f), -Vector3.UnitZ,
                enabled, visible, [], [], scratch, out _, seeds ?? new(-1, -1), scene));
        return scratch;
    }

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, portalGraphValid = true,
        groupBatches = groups, portals = [Portal()], doodads = [], wmoRenderBatches = []
    };

    private static WorldModelGroupBatches Group(uint flags, params WmoPortalLink[] links) => new()
    {
        flags = flags, mogiFlags = flags, portalLinks = links, doodadReferences = [],
        mogiBoundingBox = new(new(-0.2f, -0.2f, 0.1f), new(0.2f, 0.2f, 0.8f))
    };

    private static WmoPortalLink Link(ushort portal, ushort target, short side = 1) => new()
        { PortalIndex = portal, TargetGroupIndex = target, Side = side };

    private static WmoPortal Portal(float radius = 0.2f, float centerX = 0f, float z = 0.5f) => new()
    {
        Normal = Vector3.UnitZ, Distance = -z,
        Vertices = [new(centerX - radius, -radius, z), new(centerX + radius, -radius, z),
            new(centerX + radius, radius, z), new(centerX - radius, radius, z)]
    };

    private sealed class Vector3ToleranceComparer : System.Collections.IComparer
    {
        public int Compare(object? left, object? right) =>
            Vector3.Distance((Vector3)left!, (Vector3)right!) < 0.000001f ? 0 : 1;
    }
}
