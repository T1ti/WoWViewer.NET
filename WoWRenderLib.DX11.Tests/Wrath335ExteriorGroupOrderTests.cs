using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335ExteriorGroupOrderTests
{
    [TestMethod]
    public void BucketsAscendAndGroupsInOneBucketKeepSourceOrder()
    {
        var order = new Wrath335ExteriorGroupOrder();
        order.Build([Group(8, Box(80f)), Group(8, Box(20f)), Group(8, Box(10f)),
            Group(8, Box(40f)), Group(8, Box(-20f))], [true, true, true, true, true],
            Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] {
            new(1, 0), new(2, 0), new(4, 0), new(3, 1), new(0, 2) }, order.Seeds.ToArray());
    }

    [TestMethod]
    public void NearestBoundsCornerDeterminesBucketInsteadOfCenterOrEuclideanDistance()
    {
        var order = new Wrath335ExteriorGroupOrder();
        order.Build([Group(8, new(new(10f, 1000f, 0f), new(500f, 1002f, 1f))),
            Group(8, Box(40f))], [true, true], Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(0, 0), new(1, 1) },
            order.Seeds.ToArray());
    }

    [TestMethod]
    public void NegativeDirectionUsesMaximumCornerAndCameraRelativePlane()
    {
        var order = new Wrath335ExteriorGroupOrder();
        order.Build([Group(8, new(new(1f, 0f, 0f), new(99f, 1f, 1f))),
            Group(8, new(new(50f, 0f, 0f), new(60f, 1f, 1f)))], [true, true],
            Matrix4x4.Identity, new(100f, 0f, 0f), -Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(0, 0), new(1, 1) },
            order.Seeds.ToArray());
    }

    [TestMethod]
    public void RotationAndTranslationUseTheWorldAabbOfRootBounds()
    {
        var order = new Wrath335ExteriorGroupOrder();
        // Exact quarter turn, avoiding trigonometric fixture rounding.
        var model = new Matrix4x4(0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f, 100f, 0f, 0f, 1f);
        order.Build([Group(8, new(new(0f, 0f, 0f), new(1f, 1f, 1f))),
            Group(8, new(new(0f, 60f, 0f), new(1f, 61f, 1f)))], [true, true],
            model, Vector3.Zero, Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(1, 1), new(0, 2) },
            order.Seeds.ToArray());
    }

    [TestMethod]
    public void RootFlagsAndAvailabilitySelectSeedsIndependentlyOfLoadedFlags()
    {
        var order = new Wrath335ExteriorGroupOrder();
        order.Build([Group(8, Box(10f)) with { flags = 0 },
            Group(0, Box(10f)) with { flags = 8 }, Group(0x10000, Box(10f)),
            Group(0x40, Box(10f)), Group(8, Box(10f))], [true, true, true, true, false],
            Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(0, 0), new(2, 0) },
            order.Seeds.ToArray());
    }

    [DataTestMethod]
    [DataRow(-100f, 0, true)]
    [DataRow(0f, 0, true)]
    [DataRow(32f, 0, true)]
    [DataRow(34f, 1, true)]
    [DataRow(67f, 2, true)]
    [DataRow(100f, 2, true)]
    [DataRow(133.33334f, 4, true)]
    [DataRow(2120f, 63, true)]
    [DataRow(2133.3333f, 63, true)]
    [DataRow(2133.3335f, 0, false)]
    public void StagedFloatScaleAndNearestEvenConversionPreserveNativeBoundaries(
        float depth, int expectedBucket, bool accepted)
    {
        Assert.AreEqual(accepted, Wrath335ExteriorGroupOrder.TryGetBucket(Box(depth),
            Vector3.UnitX, new(1f, 0f, 0f, 0f), out var bucket));
        if (accepted)
            Assert.AreEqual(expectedBucket, bucket);
    }

    [TestMethod]
    public void HorizontalPlaneIgnoresPitchAndPreservesTheNearVerticalThreshold()
    {
        Assert.AreEqual(new Vector4(1f, 0f, 0f, -10f),
            Wrath335ExteriorGroupOrder.HorizontalDepthPlane(new(10f, 0f, 100f), new(1f, 0f, 10f)));
        Assert.AreEqual(Vector4.Zero,
            Wrath335ExteriorGroupOrder.HorizontalDepthPlane(Vector3.Zero, Vector3.UnitZ));
        var barelyHorizontal = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(Vector3.Zero,
            new(1f / 128f, 0f, 1f));
        Assert.IsTrue(barelyHorizontal.X < 0.01f);
        var aboveThreshold = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(Vector3.Zero,
            new(1f / 32f, 0f, 1f));
        Assert.AreEqual(1f, aboveThreshold.X);
    }

    [TestMethod]
    public void UnbucketedSeedsRetainSourceOrderAndBypassTheSixtyFourBucketCutoff()
    {
        var order = new Wrath335ExteriorGroupOrder();
        order.Build([Group(8, Box(3000f)), Group(8, Box(10f))], [true, true],
            Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX, depthSorted: false);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(0, -1), new(1, -1) },
            order.Seeds.ToArray());
        order.Build([Group(8, Box(3000f)), Group(8, Box(10f))], [true, true],
            Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        CollectionAssert.AreEqual(new Wrath335ExteriorGroupSeed[] { new(1, 0) }, order.Seeds.ToArray());
        order.Build([], [], Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX);
        Assert.AreEqual(0, order.Seeds.Length);
    }

    [TestMethod]
    public void NearerSeedEmitsSharedPortalBeforeFartherSeedBlocksAndDeduplicatesIt()
    {
        var model = Model(Group(8, Box(80f), Link(0, 2), Link(1, 2, -1)),
            Group(8, Box(10f), Link(0, 2)), Group(0, Box(0f)));
        model.portals = [Portal(0.2f), Portal(0.1f)];
        var scratch = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { true, true, true }, visible);
        Assert.AreEqual(1, scratch.ExteriorPortalViews.Forwarded.Views.Length);
        // Retaining source order lets the farther seed stamp bit 8 while its
        // blocker discards the candidate, preventing the nearer seed's view.
        scratch = Compute(model, out _, depthSorted: false);
        Assert.AreEqual(0, scratch.ExteriorPortalViews.Forwarded.Views.Length);
    }

    [TestMethod]
    public void ForwardedPolygonsFollowBucketOrderAndWithinBucketAppendOrder()
    {
        var model = Model(Group(8, Box(80f), Link(0, 3)),
            Group(8, Box(20f), Link(1, 3)), Group(8, Box(10f), Link(2, 3)), Group(0, Box(0f)));
        model.portals = [Portal(0.2f), Portal(0.1f), Portal(0.15f)];
        var scratch = Compute(model, out _);
        var views = scratch.ExteriorPortalViews.Forwarded;
        CollectionAssert.AreEqual(new[] { 0.45f, 0.425f, 0.4f },
            views.Views.ToArray().Select(v => v.Rect.MinY).ToArray());
    }

    [TestMethod]
    public void DepthCutoffAffectsOnlyWrathDepthSortedSeeds()
    {
        var model = Model(Group(8, Box(3000f), Link(0, 1)), Group(0, Box(0f)));
        _ = Compute(model, out var visible);
        CollectionAssert.AreEqual(new[] { false, false }, visible);
        _ = Compute(model, out visible, depthSorted: false);
        CollectionAssert.AreEqual(new[] { true, true }, visible);
        model.wrath335 = false;
        _ = Compute(model, out visible);
        CollectionAssert.AreEqual(new[] { true, true }, visible);
    }

    private static BoundingBox Box(float minX) => new(new(minX, -0.2f, 0.1f), new(minX + 1f, 0.2f, 0.8f));

    private static WorldModelGroupBatches Group(uint flags, BoundingBox bounds,
        params WmoPortalLink[] links) => new()
        { flags = flags, mogiFlags = flags, mogiBoundingBox = bounds, portalLinks = links, doodadReferences = [] };

    private static WmoPortalLink Link(ushort portal, ushort group, short side = 1) =>
        new() { PortalIndex = portal, TargetGroupIndex = group, Side = side };

    private static WmoPortal Portal(float radius) => new()
    {
        Normal = Vector3.UnitZ, Distance = -0.5f,
        // Keep projected X width above the native 0.001 rejection while the
        // wide camera includes groups beyond the 64-bucket depth cutoff.
        Vertices = [new(-radius * 32f, -radius, 0.5f), new(radius * 32f, -radius, 0.5f),
            new(radius * 32f, radius, 0.5f), new(-radius * 32f, radius, 0.5f)]
    };

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, portalGraphValid = true,
        groupBatches = groups, portals = [Portal(0.2f)], doodads = [], wmoRenderBatches = []
    };

    private static WmoPortalVisibilityScratch Compute(WorldModel model, out bool[] visible,
        bool depthSorted = true)
    {
        var scratch = new WmoPortalVisibilityScratch();
        visible = new bool[model.groupBatches.Length];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity,
            Matrix4x4.CreateScale(1f / 4096f, 1f, 1f), new(0f, 0f, 1f),
            Enumerable.Repeat(true, visible.Length).ToArray(), visible, [], [], scratch,
            out _, 1760f, new(-1, -1), Vector3.UnitX, depthSorted));
        return scratch;
    }
}
