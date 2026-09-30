using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335SceneExteriorGroupsTests
{
    private static readonly Matrix4x4 WideCamera = Matrix4x4.CreateScale(1f / 4096f, 1f, 1f);

    [TestMethod]
    public void GlobalBucketsInterleavePlacementsAndPreserveArrivalsWithinEachBand()
    {
        var queue = Begin();
        Add(queue, 7, [Group(80f), Group(20f), Group(10f)]);
        Add(queue, 2, [Group(40f), Group(15f)]);
        queue.Complete(true, true);
        AssertOrder(queue, (7, 1, 0), (7, 2, 0), (2, 1, 0), (2, 0, 1), (7, 0, 2));
    }

    [TestMethod]
    public void UpdatedPlacementsFollowAllBucketsInOriginalSourceOrderWithAViewer()
    {
        var queue = Begin();
        Add(queue, 0, [Group(3000f), Group(10f)], flags: 0x400);
        Add(queue, 1, [Group(80f)]);
        Add(queue, 2, [Group(20f)], flags: 0x400);
        queue.Complete(true, true);
        AssertOrder(queue, (1, 0, 2), (0, 0, -1), (0, 1, -1), (2, 0, -1));
    }

    [TestMethod]
    public void EnclosedViewerSuppressesOrdinaryBucketsButKeepsUpdatedCandidates()
    {
        var queue = Begin();
        Add(queue, 0, [Group(10f)]);
        Add(queue, 1, [Group(80f), Group(10f)], flags: 0x400);
        queue.Complete(true, false);
        AssertOrder(queue, (1, 0, -1), (1, 1, -1));
    }

    [TestMethod]
    public void NoViewerRebucketsUpdatedNodesAfterOrdinaryArrivalsWithinEachBand()
    {
        var queue = Begin();
        Add(queue, 0, [Group(20f), Group(80f)], flags: 0x400);
        Add(queue, 1, [Group(10f), Group(67f)]);
        queue.Complete(false, true);
        AssertOrder(queue, (1, 0, 0), (0, 0, 0), (1, 1, 2), (0, 1, 2));
    }

    [TestMethod]
    public void FirstRebucketCutoffDropsLaterUpdatedNodesWhileOrdinaryCutoffsSkipOne()
    {
        var queue = Begin();
        Add(queue, 0, [Group(10f), Group(3000f), Group(20f)], flags: 0x400);
        Add(queue, 1, [Group(3000f), Group(40f)]);
        Add(queue, 2, [Group(10f)], flags: 0x400);
        queue.Complete(false, true);
        AssertOrder(queue, (0, 0, 0), (1, 1, 1));
    }

    [DataTestMethod]
    [DataRow(0u, true, 1)]
    [DataRow(0x80u, true, 1)]
    [DataRow(0x20u, true, 0)]
    [DataRow(0x420u, true, 0)]
    [DataRow(0u, false, 0)]
    public void PreparedAvailabilityAndRuntimeSkipGateSources(uint flags, bool prepared, int expected)
    {
        var queue = Begin();
        Add(queue, 0, [Group(10f)], flags, prepared);
        queue.Complete(true, true);
        Assert.AreEqual(expected, queue.Seeds.Length);
    }

    [TestMethod]
    public void RootFlagsAvailabilityAndWorldCameraBoundsGateEachSource()
    {
        var queue = Begin();
        queue.AddPlacement(0, [Group(10f) with { mogiFlags = 0, flags = 8 },
            Group(10f) with { mogiFlags = 0x10000, flags = 0 },
            Group(10f), Group(10f) with { mogiBoundingBox = new(new(0f, 2f, 0f), new(1f, 3f, 1f)) }],
            [true, true, false, true], Matrix4x4.Identity, 0, true);
        Add(queue, 1, [Group(10f)], model: Matrix4x4.CreateTranslation(0f, 3f, 0f));
        queue.Complete(true, true);
        AssertOrder(queue, (0, 1, 0));
    }

    [TestMethod]
    public void CameraBoundsEncloseAllEightDx11CornersAndIncludeTouchingSources()
    {
        var viewProjection = Matrix4x4.CreateTranslation(-20f, -30f, -40f) *
            Matrix4x4.CreateScale(0.5f, 0.25f, 0.125f);
        Assert.IsTrue(Wrath335SceneExteriorGroups.TryGetCameraBounds(viewProjection, out var bounds));
        Assert.AreEqual(new Vector3(18f, 26f, 40f), bounds.Min);
        Assert.AreEqual(new Vector3(22f, 34f, 48f), bounds.Max);
        var queue = new Wrath335SceneExteriorGroups();
        queue.Begin(new(20f, 30f, 40f), Vector3.UnitX, viewProjection);
        queue.AddPlacement(0, [Group(0f) with { mogiBoundingBox = new(new(22f, 34f, 48f), new(23f, 35f, 49f)) },
            Group(0f) with { mogiBoundingBox = new(new(22.001f, 34f, 48f), new(23f, 35f, 49f)) }],
            [true, true], Matrix4x4.Identity, 0, true);
        queue.Complete(true, true);
        AssertOrder(queue, (0, 0, 0));
    }

    [TestMethod]
    public void PerspectiveCameraBoundsUseFarCornersRatherThanEyeOrDistanceSphere()
    {
        var camera = Matrix4x4.CreateLookAt(new(10f, 20f, 30f), new(10f, 20f, 29f), Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 2f, 1f, 10f);
        Assert.IsTrue(Wrath335SceneExteriorGroups.TryGetCameraBounds(camera, out var bounds));
        Assert.AreEqual(-10f, bounds.Min.X, 0.001f);
        Assert.AreEqual(10f, bounds.Min.Y, 0.001f);
        Assert.AreEqual(20f, bounds.Min.Z, 0.001f);
        Assert.AreEqual(30f, bounds.Max.X, 0.001f);
        Assert.AreEqual(30f, bounds.Max.Y, 0.001f);
        Assert.AreEqual(29f, bounds.Max.Z, 0.001f);
    }

    [TestMethod]
    public void UnsupportedCameraBoundsFailOpenAndBeginningAnotherFrameDropsSources()
    {
        Assert.IsFalse(Wrath335SceneExteriorGroups.TryGetCameraBounds(default, out _));
        var nonfinite = Matrix4x4.Identity;
        nonfinite.M11 = float.NaN;
        Assert.IsFalse(Wrath335SceneExteriorGroups.TryGetCameraBounds(nonfinite, out _));
        var queue = new Wrath335SceneExteriorGroups();
        queue.Begin(Vector3.Zero, Vector3.UnitX, default);
        Add(queue, 0, [Group(10f) with { mogiBoundingBox = new(new(10f, 200f, 0f), new(11f, 201f, 1f)) }]);
        queue.Complete(true, true);
        AssertOrder(queue, (0, 0, 0));
        queue.Begin(Vector3.Zero, Vector3.UnitX, WideCamera);
        queue.Complete(true, true);
        Assert.AreEqual(0, queue.Seeds.Length);
    }

    [DataTestMethod]
    [DataRow(1f, 0f, 0f, true)]
    [DataRow(1f, 1f, 1f, true)]
    [DataRow(1.001f, 0f, 0f, false)]
    [DataRow(0f, 1.001f, 0f, false)]
    [DataRow(0f, 0f, 1.001f, false)]
    public void UnbucketedOverlapIncludesFacesAndCornersWithoutTolerance(float x, float y, float z, bool expected)
    {
        var candidate = new BoundingBox(new(x, y, z), new(x + 1f, y + 1f, z + 1f));
        Assert.AreEqual(expected, Wrath335SceneExteriorGroups.AcceptUnbucketed(candidate, false,
            [new(Vector3.Zero, Vector3.One)]));
        Assert.IsTrue(Wrath335SceneExteriorGroups.AcceptUnbucketed(candidate, true, []));
        Assert.IsFalse(Wrath335SceneExteriorGroups.AcceptUnbucketed(candidate, false, []));
    }

    private static Wrath335SceneExteriorGroups Begin()
    {
        var queue = new Wrath335SceneExteriorGroups();
        queue.Begin(Vector3.Zero, Vector3.UnitX, WideCamera);
        return queue;
    }

    private static void Add(Wrath335SceneExteriorGroups queue, int placement,
        WorldModelGroupBatches[] groups, uint flags = 0, bool prepared = true, Matrix4x4? model = null) =>
        queue.AddPlacement(placement, groups, Enumerable.Repeat(true, groups.Length).ToArray(),
            model ?? Matrix4x4.Identity, flags, prepared);

    private static WorldModelGroupBatches Group(float depth) => new()
    {
        mogiFlags = 8,
        mogiBoundingBox = new(new(depth, -0.2f, 0.1f), new(depth + 1f, 0.2f, 0.8f))
    };

    private static void AssertOrder(Wrath335SceneExteriorGroups queue,
        params (int Placement, int Group, int Bucket)[] expected) =>
        CollectionAssert.AreEqual(expected, queue.Seeds.ToArray()
            .Select(s => (s.PlacementIndex, s.GroupIndex, s.Bucket)).ToArray());
}
