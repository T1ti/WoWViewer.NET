using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoBspRaycasterTests
{
    private static readonly BoundingBox Bounds = new(new(-2f), new(2f));
    private static readonly Vector3[] Floor =
        [new(-1f, -1f, 0f), new(1f, -1f, 0f), new(0f, 1f, 0f)];

    [TestMethod]
    public void ViewerUsesOnlyLeavesReachedByItsSegment()
    {
        Vector3[] vertices = [.. Floor, new(-1f, -1f, 0.5f),
            new(1f, -1f, 0.5f), new(0f, 1f, 0.5f)];
        var tree = SplitTree([0, 1, 2, 3, 4, 5]);
        var context = Context(new(0.25f, 0f, 1f), -Vector3.UnitZ);
        Assert.IsTrue(Query(context, vertices, tree, out var hit));
        Assert.AreEqual(0, hit.FaceIndex);
        Assert.AreEqual(1f, hit.WorldDistance, 0.000001f);
        Assert.AreEqual(1, hit.TestedFaces);
    }

    [TestMethod]
    public void CrossingRayVisitsNearChildFirstAndLastEqualHitWins()
    {
        var tree = SplitTree([0, 1, 2, 0, 1, 2]);
        var fromPositive = Context(new(1f, 0f, 1f), new(-1f, 0f, -1f));
        Assert.IsTrue(Query(fromPositive, Floor, tree, out var positiveHit));
        Assert.AreEqual(1, positiveHit.FaceIndex);
        var fromNegative = Context(new(-1f, 0f, 1f), new(1f, 0f, -1f));
        Assert.IsTrue(Query(fromNegative, Floor, tree, out var negativeHit));
        Assert.AreEqual(0, negativeHit.FaceIndex);
        Assert.AreEqual(2, negativeHit.TestedFaces);
    }

    [TestMethod]
    public void EndpointWithinPlaneToleranceVisitsBothChildrenPositiveFirst()
    {
        var tree = SplitTree([0, 1, 2, 0, 1, 2]);
        var context = Context(new(-0.009f, 0f, 1f), -Vector3.UnitZ);
        Assert.IsTrue(Query(context, Floor, tree, out var hit));
        Assert.AreEqual(1, hit.FaceIndex);
        Assert.AreEqual(2, hit.TestedFaces);
        context = Context(new(-0.011f, 0f, 1f), -Vector3.UnitZ);
        Assert.IsTrue(Query(context, Floor, tree, out hit));
        Assert.AreEqual(1, hit.FaceIndex);
        Assert.AreEqual(1, hit.TestedFaces);
    }

    [TestMethod]
    public void DuplicateFacesAreTestedOnceAndScratchIsReusable()
    {
        var tree = LeafTree([0, 1, 2], [0, 0, 0]);
        var scratch = new WmoBspRayScratch();
        var context = Context(new(0f, 0f, 1f), -Vector3.UnitZ);
        Assert.IsTrue(Query(context, Floor, tree, out var first, scratch));
        Assert.IsTrue(Query(context, Floor, tree, out var second, scratch));
        Assert.AreEqual(1, first.TestedFaces);
        Assert.AreEqual(first, second);
        Assert.AreEqual((byte)0, tree.FaceFlags[0]);
    }

    [TestMethod]
    public void ClientFaceLimitPreservesHitBeforeUnvisitedCloserFace()
    {
        var count = Wrath335WmoBspRaycaster.FaceLimit + 1;
        var indices = new ushort[count * 3];
        for (var face = 0; face < count; face++)
        {
            var vertex = face == count - 1 ? 3 : 0;
            indices[face * 3] = (ushort)vertex;
            indices[face * 3 + 1] = (ushort)(vertex + 1);
            indices[face * 3 + 2] = (ushort)(vertex + 2);
        }
        var tree = LeafTree(indices,
            Enumerable.Range(0, count).Select(i => (ushort)i).ToArray());
        Vector3[] vertices = [.. Floor, new(-1f, -1f, 0.5f),
            new(1f, -1f, 0.5f), new(0f, 1f, 0.5f)];
        Assert.IsTrue(Query(Context(new(0f, 0f, 1f), -Vector3.UnitZ),
            vertices, tree, out var hit));
        Assert.AreEqual(Wrath335WmoBspRaycaster.FaceLimit - 1, hit.FaceIndex);
        Assert.AreEqual(1f, hit.WorldDistance, 0.000001f);
        Assert.AreEqual(Wrath335WmoBspRaycaster.FaceLimit, hit.TestedFaces);
        Assert.IsTrue(hit.FaceLimitReached);
    }

    [TestMethod]
    public void CameraMaskRejectsVisitedBitButAcceptsCollisionAndDetailFlags()
    {
        var tree = LeafTree([0, 1, 2, 0, 1, 2, 0, 1, 2], [0, 1, 2]);
        tree.FaceFlags[0] = 0x80;
        tree.FaceFlags[1] = 0x08;
        tree.FaceFlags[2] = 0x04;
        Assert.IsTrue(Query(Context(new(0f, 0f, 1f), -Vector3.UnitZ),
            Floor, tree, out var hit));
        Assert.AreEqual(2, hit.FaceIndex);
        Assert.AreEqual(2, hit.TestedFaces);
    }

    [TestMethod]
    public void UpperClipPlaneUsesClientAsymmetricTolerance()
    {
        var tree = SplitTree([0, 1, 2, 0, 1, 2]);
        var vertices = Floor.Select(p => p + new Vector3(2f, 0f, 0f)).ToArray();
        Assert.IsFalse(Query(Context(new(1.995f, 0f, 1f), -Vector3.UnitZ),
            vertices, tree, out _));
        Assert.IsTrue(Query(Context(new(1.98f, 0f, 1f), -Vector3.UnitZ),
            vertices, tree, out _));
    }

    [TestMethod]
    public void DefaultDigestRejectsFaceBeyondRayBoxAndStillCountsVisitedFace()
    {
        Vector3[] vertices = [Vector3.Zero, new(100f, 0f, 0f), new(0f, 100f, 0f)];
        var context = Context(new(-0.1f, 1f, 1f), -Vector3.UnitZ);
        var tree = LeafTree([0, 1, 2], [0, 0]);
        Assert.IsFalse(Query(context, vertices, tree, out var culled));
        Assert.AreEqual(1, culled.TestedFaces);
        // Barycentric tolerance alone accepts this point outside a large face.
        Assert.IsTrue(Wrath335WmoBspRaycaster.TryIntersect(context, vertices,
            tree, Bounds, new(), 10f, 10f, out _, useDigestCache: false));
    }

    [TestMethod]
    public void LargeLeafFallsBackToRawFacesInsteadOfDigestOutcodes()
    {
        Vector3[] vertices = [Vector3.Zero, new(100f, 0f, 0f), new(0f, 100f, 0f)];
        var context = Context(new(-0.1f, 1f, 1f), -Vector3.UnitZ);
        var cached = LeafTree([0, 1, 2], new ushort[300]);
        var raw = LeafTree([0, 1, 2], new ushort[301]);
        Assert.IsFalse(Query(context, vertices, cached, out _));
        Assert.IsTrue(Query(context, vertices, raw, out var hit));
        Assert.AreEqual(1, hit.TestedFaces);
    }

    [TestMethod]
    public void NearestDistanceUsesWorldUnitsAndAcceptsExactMaximum()
    {
        var context = Context(new(0f, 0f, 3f), -Vector3.UnitZ,
            Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(0f, 0f, 1f));
        var tree = LeafTree([0, 1, 2], [0]);
        Assert.IsTrue(Query(context, Floor, tree, out var hit, maximum: 2f));
        Assert.AreEqual(2f, hit.WorldDistance, 0.000001f);
        Assert.IsFalse(Query(context, Floor, tree, out _, maximum: 1.99f));
    }

    [TestMethod]
    public void MissingChildrenInvalidReferencesAndCyclesDoNotProduceHits()
    {
        var context = Context(new(0f, 0f, 1f), -Vector3.UnitZ);
        Assert.IsFalse(Query(context, Floor,
            new([new(0, ushort.MaxValue, ushort.MaxValue, 0, 0, 0f)],
                [], [0, 1, 2], [0]), out _));
        Assert.IsFalse(Query(context, Floor, LeafTree([0, 1, 2], [9]), out _));
        Assert.IsFalse(Query(context, Floor,
            new([new(0, 0, 0, 0, 0, 0f)], [], [0, 1, 2], [0]), out _));
    }

    [TestMethod]
    public void CpuViewerQueryRoutesWrathTreeAndKeepsModernTrianglePath()
    {
        Vector3[] vertices = [.. Floor, new(-1f, -1f, 0.5f),
            new(1f, -1f, 0.5f), new(0f, 1f, 0.5f)];
        var wmo = new WorldModel
        {
            legacyLighting = true,
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    flags = 0x2000, boundingBox = Bounds, mogiBoundingBox = Bounds,
                    raycastVertices = vertices, raycastIndices = [0, 1, 2, 3, 4, 5],
                    viewerBsp = SplitTree([0, 1, 2, 3, 4, 5]), portalLinks = []
                }
            ],
            portals = []
        };
        var scratch = new WmoPortalVisibilityScratch();
        Assert.IsFalse(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, new(0.25f, 0f, 1f), [true], scratch,
            out _, maximumViewerDistance: 0.75f));
        wmo.legacyLighting = false;
        Assert.IsTrue(WmoPortalVisibility.TryLocateViewerGroup(wmo,
            Matrix4x4.Identity, new(0.25f, 0f, 1f), [true], scratch,
            out _, maximumViewerDistance: 0.75f));
    }

    private static WmoBspTree SplitTree(ushort[] indices) =>
        new([new(0, 2, 1, 0, 0, 0f),
             new(4, ushort.MaxValue, ushort.MaxValue, 1, 0, 0f),
             new(4, ushort.MaxValue, ushort.MaxValue, 1, 1, 0f)],
            [0, 1], indices, [0, 0]);

    private static WmoBspTree LeafTree(ushort[] indices, ushort[] faces) =>
        new([new(4, ushort.MaxValue, ushort.MaxValue, (ushort)faces.Length, 0, 0f)],
            faces, indices, new byte[indices.Length / 3]);

    private static TriangleRaycastContext Context(
        Vector3 origin, Vector3 direction, Matrix4x4? model = null)
    {
        Assert.IsTrue(TriangleMeshRaycaster.TryCreateContext(
            new Ray(origin, Vector3.Normalize(direction)),
            model ?? Matrix4x4.Identity, out var context));
        return context;
    }

    private static bool Query(
        TriangleRaycastContext context, Vector3[] vertices, WmoBspTree tree,
        out WmoBspRayHit hit, WmoBspRayScratch? scratch = null, float maximum = 10f) =>
        Wrath335WmoBspRaycaster.TryIntersect(context, vertices, tree, Bounds,
            scratch ?? new WmoBspRayScratch(), maximum, 10f, out hit);
}
