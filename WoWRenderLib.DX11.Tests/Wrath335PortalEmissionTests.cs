using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalEmissionTests
{
    [TestMethod]
    public void ClosedInteriorEmitsNothingAndExteriorPortalOpensBothViews()
    {
        var model = Model(Group(0), Group(8));
        var views = Compute(model, out var visible, out _);
        Assert.IsFalse(views.HasSkyView);
        CollectionAssert.AreEqual(new[] { true, false }, visible);
        Link(ref model, 0, 1, 0);
        model.portals = [Portal(0.2f)];
        views = Compute(model, out visible, out _);
        Assert.IsTrue(views.HasSkyView);
        Assert.IsTrue(views.HasExteriorView);
        Assert.AreEqual(0.5f, views.ExteriorDistance);
        CollectionAssert.AreEqual(new[] { true, true }, visible);
    }

    [TestMethod]
    public void PortalFlagsUseMogiAndSkyOnlyDestinationContinuesTraversal()
    {
        var model = Model(Group(0), Group(0) with { mogiFlags = 0x100 }, Group(0));
        Link(ref model, 0, 1, 0);
        Link(ref model, 1, 2, 1);
        model.portals = [Portal(0.2f), Portal(0.1f)];
        var views = Compute(model, out var visible, out var traversed);
        Assert.IsTrue(views.HasSkyView);
        Assert.IsFalse(views.HasExteriorView);
        CollectionAssert.AreEqual(new[] { true, true, true }, visible);
        Assert.AreEqual(2, traversed);
    }

    [TestMethod]
    public void AlwaysDrawDestinationEmitsBeforeTraversalStops()
    {
        var model = Model(Group(0), Group(0x10000));
        Link(ref model, 0, 1, 0);
        model.portals = [Portal(0.2f)];
        Assert.IsTrue(Compute(model, out _, out _).HasExteriorView);
    }

    [TestMethod]
    public void SharedPortalEmissionIsDeduplicatedAcrossDestinations()
    {
        var model = Model(Group(0), Group(0x100), Group(8));
        model.groupBatches[0] = model.groupBatches[0] with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 },
                new() { PortalIndex = 0, TargetGroupIndex = 2, Side = 1 }]
        };
        model.portals = [Portal(0.2f)];
        var views = Compute(model, out _, out _);
        Assert.IsTrue(views.HasSkyView);
        Assert.IsFalse(views.HasExteriorView);
        Assert.AreEqual(1, views.Windows.Length);
    }

    [TestMethod]
    public void EmittedViewUsesOffsetPolygonWithoutIntersectingParentRectangle()
    {
        var model = Model(Group(0), Group(0), Group(0x100));
        Link(ref model, 0, 1, 0);
        Link(ref model, 1, 2, 1);
        model.portals = [Portal(0.1f), Portal(0.6f)];
        var views = Compute(model, out _, out _);
        Assert.AreEqual(new WmoPortalRect(-0.6f, -0.6f, 0.6f, 0.6f), views.SkyRect);
        var projection = Matrix4x4.CreateLookAt(new(0f, 0f, 1f), Vector3.Zero, Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 10f);
        model = Model(Group(0), Group(0x100));
        Link(ref model, 0, 1, 0);
        model.portals = [Portal(0.2f)];
        views = Compute(model, out _, out _, projection);
        Assert.AreEqual(0.2f / 0.51f, views.SkyRect.MaxX, 0.000001f);
        Assert.AreEqual(0.5f, views.SkyDistance);
    }

    [TestMethod]
    public void BackFacingPortalDoesNotEmitAndFrameScratchIsReusable()
    {
        var model = Model(Group(0), Group(8));
        model.portals = [Portal(0.2f)];
        model.groupBatches[0] = model.groupBatches[0] with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }]
        };
        Assert.IsFalse(Compute(model, out _, out _).HasSkyView);
        var scratch = new WmoPortalVisibilityScratch();
        Link(ref model, 0, 1, 0);
        Assert.IsTrue(Compute(model, out _, out _, scratch: scratch).HasSkyView);
        var emittedBuffer = scratch.EmittedPortalViews;
        Assert.IsTrue(Compute(model, out _, out _, scratch: scratch).HasSkyView);
        Assert.AreSame(emittedBuffer, scratch.EmittedPortalViews);
        Assert.AreEqual(0u, scratch.ViewerBsp.Epoch);
    }

    [TestMethod]
    public void ExteriorSeedsUseTheEmittedWindowInsteadOfTheFullCameraFrustum()
    {
        var model = Model(Group(0), Group(8), Group(8) with
        {
            mogiBoundingBox = new(new(0.7f, -0.1f, 0.2f), new(0.9f, 0.1f, 0.8f))
        });
        Link(ref model, 0, 1, 0);
        model.portals = [Portal(0.2f)];
        var views = Compute(model, out var visible, out _);
        Assert.IsTrue(views.HasExteriorView);
        CollectionAssert.AreEqual(new[] { true, true, false }, visible);
    }

    [TestMethod]
    public void UnavailableDestinationStillEmitsItsRootMetadataViewBeforeLoadedGroupLookup()
    {
        var model = Model(Group(0), Group(8));
        Link(ref model, 0, 1, 0);
        model.portals = [Portal(0.2f)];
        var views = new Wrath335PortalSceneViews();
        views.Reset(true);
        bool[] visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryComputeViewerScene(model, Matrix4x4.Identity,
            Matrix4x4.Identity, new(0f, 0f, 1f), -Vector3.UnitZ, [true, false],
            visible, [], [], new(), out _, new(0, -1), views));
        Assert.IsTrue(views.HasExteriorView);
        CollectionAssert.AreEqual(new[] { true, false }, visible);
    }

    [TestMethod]
    public void ThreeOrMoreProjectedVerticesRetainAZeroAreaInteriorWindow()
    {
        var model = Model(Group(0), Group(0x100));
        Link(ref model, 0, 1, 0);
        model.portals = [new() { Normal = Vector3.UnitX, Distance = -0.5f,
            Vertices = [new(0.5f, -0.2f, 0.3f), new(0.5f, 0.2f, 0.3f),
                new(0.5f, 0.2f, 0.7f), new(0.5f, -0.2f, 0.7f)] }];
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(true);
        var visible = new bool[2];
        Assert.IsTrue(WmoPortalVisibility.TryComputeViewerScene(model, Matrix4x4.Identity,
            Matrix4x4.Identity, new(0.5f, 0f, 0.5f), -Vector3.UnitZ, [true, true],
            visible, [], [], new(), out _, new(0, -1), scene));
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        Assert.AreEqual(1, scene.Windows.Length);
        Assert.AreEqual(new WmoPortalRect(0.745f, 0.4f, 0.745f, 0.6f), scene.Windows[0].Rect);
        Assert.IsTrue(scene.HasSkyView);
        scene.BuildComplement();
        Assert.AreEqual(4, scene.Complement.Views.Length);
    }

    [TestMethod]
    public void BothRegisteredViewerGroupsAppendInteriorWindowsInTheirTraversalOrder()
    {
        var model = Model(Group(0), Group(0), Group(0x100), Group(0x40000));
        Link(ref model, 0, 2, 0);
        Link(ref model, 1, 3, 1);
        model.portals = [Portal(0.1f), Portal(0.5f)];
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(true);
        var visible = new bool[4];
        Assert.IsTrue(WmoPortalVisibility.TryComputeViewerScene(model, Matrix4x4.Identity,
            Matrix4x4.Identity, new(0f, 0f, 1f), -Vector3.UnitZ, [true, true, true, true],
            visible, [], [], new(), out _, new(0, 1), scene));
        CollectionAssert.AreEqual(new[] { true, true, true, true }, visible);
        CollectionAssert.AreEqual(new Wrath335PortalWindow[] {
            new(new(0.45f, 0.45f, 0.55f, 0.55f), 0.5f),
            new(new(0.25f, 0.25f, 0.75f, 0.75f), 0.5f) }, scene.Windows.ToArray());
    }

    private static Wrath335PortalSceneViews Compute(WorldModel model,
        out bool[] visible, out int traversed, Matrix4x4? projection = null,
        WmoPortalVisibilityScratch? scratch = null)
    {
        var views = new Wrath335PortalSceneViews();
        views.Reset(true);
        visible = new bool[model.groupBatches.Length];
        Assert.IsTrue(WmoPortalVisibility.TryComputeViewerScene(model, Matrix4x4.Identity,
            projection ?? Matrix4x4.Identity, new(0f, 0f, 1f), -Vector3.UnitZ,
            Enumerable.Repeat(true, visible.Length).ToArray(), visible, [], [],
            scratch ?? new(), out traversed, new(0, -1), views));
        return views;
    }

    private static void Link(ref WorldModel model, int from, ushort to, ushort portal) =>
        model.groupBatches[from] = model.groupBatches[from] with
        {
            portalLinks = [new() { PortalIndex = portal, TargetGroupIndex = to, Side = 1 }]
        };

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, portalGraphValid = true,
        groupBatches = groups, portals = [], doodads = [], wmoRenderBatches = []
    };

    private static WorldModelGroupBatches Group(uint flags) => new()
    {
        flags = flags, mogiFlags = flags,
        mogiBoundingBox = new(new(-0.2f, -0.2f, 0.1f), new(0.2f, 0.2f, 0.8f)),
        portalLinks = [], doodadReferences = []
    };

    private static WmoPortal Portal(float radius) => new()
    {
        Normal = Vector3.UnitZ, Distance = -0.5f,
        Vertices = [new(-radius, -radius, 0.5f), new(radius, -radius, 0.5f),
            new(radius, radius, 0.5f), new(-radius, radius, 0.5f)]
    };
}
