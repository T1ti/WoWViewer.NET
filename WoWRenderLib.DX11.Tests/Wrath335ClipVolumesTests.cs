using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335ClipVolumesTests
{
    [TestMethod]
    public void OccluderWindingDoesNotChangeWhichSideBehindThePolygonIsHidden()
    {
        var volumes = SquareVolume();
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 1f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(3f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 3f, 3f)]));
        volumes.Clear();
        volumes.AddPolygon(Vector3.Zero, Square(2f).Reverse().ToArray(), Vector3.UnitZ, 0f, false);
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 1f)]));
    }

    [TestMethod]
    public void EyeOnTheOtherSideFlipsTheEntirePlaneRangeAndRetainsEarlierVolumes()
    {
        var volumes = SquareVolume();
        volumes.AddPolygon(new(10f, 0f, 4f), Square(2f, 10f), -Vector3.UnitZ, 0f, false);
        Assert.IsTrue(volumes.ContainsPolygon([new(10f, 0f, 1f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(10f, 0f, 3f)]));
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
    }

    [TestMethod]
    public void EveryPolygonVertexMustFitOneVolumeRatherThanTheUnionOfVolumes()
    {
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, Square(2f, -4f), Vector3.UnitZ, 0f, false);
        volumes.AddPolygon(Vector3.Zero, Square(2f, 4f), Vector3.UnitZ, 0f, false);
        Assert.IsTrue(volumes.ContainsPolygon([new(-6f, 0f, 3f)]));
        Assert.IsTrue(volumes.ContainsPolygon([new(6f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(-6f, 0f, 3f), new(6f, 0f, 3f)]));
    }

    [TestMethod]
    public void PolygonAndSphereContactsAreInclusiveWithNoOcclusionTolerance()
    {
        var volumes = SquareVolume();
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 2f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 1.99999f)]));
        Assert.IsTrue(volumes.ContainsSphere(new(0f, 0f, 3f, 1f)));
        Assert.IsFalse(volumes.ContainsSphere(new(0f, 0f, 3f, 1.00001f)));
        Assert.IsFalse(volumes.ContainsSphere(new(2f, 0f, 3f, 0.1f)));
    }

    [TestMethod]
    public void EmptyVolumeSetDoesNotCullAndResetRemovesBothPlaneAndVolumeRanges()
    {
        var volumes = new Wrath335ClipVolumes();
        Assert.IsFalse(volumes.ContainsPolygon([]));
        Assert.IsFalse(volumes.ContainsSphere(Vector4.Zero));
        volumes.AddPolygon(Vector3.Zero, Square(2f), Vector3.UnitZ, 0f, false);
        Assert.IsTrue(volumes.ContainsPolygon([])); // Native predicate is vacuously true for an active volume.
        volumes.Clear();
        Assert.AreEqual(0, volumes.Planes.Length);
        Assert.AreEqual(0, volumes.Volumes.Length);
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
    }

    [TestMethod]
    public void PositiveExteriorDistanceClipsForegroundOccludersUnlessBitOneBypassesIt()
    {
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, Square(2f), Vector3.UnitZ, 3f, true);
        Assert.IsFalse(volumes.ContainsSphere(new(0f, 0f, 4f, 0f)));
        volumes.AddPolygon(Vector3.Zero, Square(2f), Vector3.UnitZ, 3f, false);
        Assert.IsTrue(volumes.ContainsSphere(new(0f, 0f, 4f, 0f)));
        volumes.Clear();
        volumes.AddPolygon(Vector3.Zero, Square(2f), Vector3.UnitZ, 2f, true);
        Assert.IsTrue(volumes.ContainsSphere(new(0f, 0f, 4f, 0f))); // Coplanar vertices are retained.
    }

    [DataTestMethod]
    [DataRow(-1f, true)]
    [DataRow(0f, true)]
    [DataRow(0.000001f, true)]
    [DataRow(0.000002f, false)]
    public void DepthClipBranchUsesTheStrictOneMicroUnitGate(float distance, bool hidden)
    {
        var volumes = new Wrath335ClipVolumes();
        Vector3[] tiny = [new(-0.001f, -0.001f, 0.0000005f), new(0.001f, -0.001f, 0.0000005f),
            new(0.001f, 0.001f, 0.0000005f), new(-0.001f, 0.001f, 0.0000005f)];
        // Both outlines are coplanar within the clip tolerance; only the active
        // depth branch removes these tiny side planes.
        volumes.AddPolygon(Vector3.Zero, tiny, Vector3.UnitZ, distance, true);
        Assert.AreEqual(hidden, volumes.ContainsPolygon([new(0f, 0f, 0.1f)]));
    }

    [TestMethod]
    public void TiltedPolygonProducesSidePlanesFromTheDepthClippedOutline()
    {
        var volumes = new Wrath335ClipVolumes();
        Vector3[] polygon = [new(-1f, -1f, 1f), new(1f, -1f, 3f),
            new(1f, 1f, 3f), new(-1f, 1f, 1f)];
        volumes.AddPolygon(Vector3.Zero, polygon, Vector3.UnitZ, 2f, true);
        Assert.IsFalse(volumes.ContainsPolygon([new(-0.5f, 0f, 3f)]));
        Assert.IsTrue(volumes.ContainsPolygon([new(0.5f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(0.5f, 0f, 2f)])); // In front of the original tilted cap.
    }

    [DataTestMethod]
    [DataRow(0.0049f, 4)]
    [DataRow(0.0051f, 5)]
    public void TinySidePlanesAreSkippedOnlyInTheDepthClippedBranch(float edge, int clippedPlanes)
    {
        Vector3[] polygon = [new(0f, 0f, 2f), new(edge, 0f, 2f), new(1f, 1f, 2f), new(-1f, 1f, 2f)];
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, polygon, Vector3.UnitZ, 2f, true);
        Assert.AreEqual(clippedPlanes, volumes.Volumes[0].PlaneCount);
        volumes.Clear();
        volumes.AddPolygon(Vector3.Zero, polygon, Vector3.UnitZ, 0f, false);
        Assert.AreEqual(5, volumes.Volumes[0].PlaneCount);
    }

    [TestMethod]
    public void NoSurvivingSidePlaneDoesNotAppendAnOrphanBaseFacet()
    {
        Vector3[] tiny = [new(-0.001f, -0.001f, 2f), new(0.001f, -0.001f, 2f),
            new(0.001f, 0.001f, 2f), new(-0.001f, 0.001f, 2f)];
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, tiny, Vector3.UnitZ, 2f, true);
        Assert.AreEqual(0, volumes.Planes.Length);
        Assert.IsFalse(volumes.ContainsSphere(new(0f, 0f, 4f, 0f)));
    }

    [TestMethod]
    public void MapAndShadowSubsetFlagsSelectSourcesAndRebuildEachFrame()
    {
        var sources = new Wrath335StaticOccluder[] { new(42, 0, Square(2f)),
            new(42, 2, Square(2f, 4f)), new(43, 1, Square(2f, -4f)) };
        var volumes = new Wrath335ClipVolumes();
        var camera = Matrix4x4.CreateScale(0.1f);
        volumes.Prepare(sources, 42, Vector3.Zero, Vector3.UnitZ, camera, 0f);
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
        Assert.IsTrue(volumes.ContainsPolygon([new(6f, 0f, 3f)]));
        Assert.IsFalse(volumes.ContainsPolygon([new(-6f, 0f, 3f)]));
        volumes.Prepare(sources, 42, Vector3.Zero, Vector3.UnitZ, camera, 0f, shadowOnly: true);
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 3f)]));
        Assert.IsTrue(volumes.ContainsPolygon([new(6f, 0f, 3f)]));
        volumes.Prepare(sources, -1, Vector3.Zero, Vector3.UnitZ, camera, 0f);
        Assert.IsFalse(volumes.ContainsPolygon([new(6f, 0f, 3f)]));
    }

    [TestMethod]
    public void SourceBoundsExpandFromZeroAndCameraUsesWorldPlaneTolerance()
    {
        var source = new Wrath335StaticOccluder(42, 0, [new(10f, 2f, 0.5f),
            new(11f, 2f, 0.5f), new(11f, 3f, 0.5f), new(10f, 3f, 0.5f)]);
        Assert.AreEqual(Vector3.Zero, source.Bounds.Min);
        Assert.AreEqual(new Vector3(11f, 3f, 0.5f), source.Bounds.Max);
        var volumes = new Wrath335ClipVolumes();
        var camera = Matrix4x4.CreateTranslation(-12.019f, -2.5f, 0f);
        volumes.Prepare([source], 42, new(10f, 2f, 1f), -Vector3.UnitZ, camera, 0f);
        Assert.AreEqual(1, volumes.Volumes.Length); // Touches right of source by 0.019, within native tolerance.
        camera.M41 = -12.020f;
        volumes.Prepare([source], 42, new(10f, 2f, 1f), -Vector3.UnitZ, camera, 0f);
        Assert.AreEqual(0, volumes.Volumes.Length);
    }

    [TestMethod]
    public void FullDirectionDepthClippingDoesNotFlattenCameraPitch()
    {
        var volumes = new Wrath335ClipVolumes();
        var source = new Wrath335StaticOccluder(42, 0, Square(2f));
        volumes.Prepare([source], 42, Vector3.Zero, new(0f, 0f, 4f),
            Matrix4x4.CreateScale(0.1f), 3f);
        Assert.IsFalse(volumes.ContainsPolygon([new(0f, 0f, 4f)]));
        volumes.Prepare([source], 42, Vector3.Zero, new(0f, 0f, 4f),
            Matrix4x4.CreateScale(0.1f), 1f);
        Assert.IsTrue(volumes.ContainsPolygon([new(0f, 0f, 4f)]));
    }

    [TestMethod]
    public void GroupSphereUsesLocalRootRadiusAndTransformedMidpointInsteadOfWorldAabbRadius()
    {
        var bounds = new BoundingBox(new(-1f, -2f, -2f), new(1f, 2f, 2f));
        var matrix = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateRotationZ(0.7f) *
            Matrix4x4.CreateTranslation(10f, 20f, 30f);
        Assert.AreEqual(new Vector4(10f, 20f, 30f, 3f), Wrath335ClipVolumes.GroupSphere(bounds, matrix));
    }

    [TestMethod]
    public void ProjectionTestsOffsetWorldPointsAndPreservesTheInteriorBypass()
    {
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(new(0f, 0f, 1f), Square(0.75f), -Vector3.UnitZ, 0f, false);
        var portal = Wrath335PortalProjectionTests.Square(0.5f) with
        { Vertices = [new(-0.1f, -0.1f, 0.5f), new(0.1f, -0.1f, 0.5f),
            new(0.1f, 0.1f, 0.5f), new(-0.1f, 0.1f, 0.5f)] };
        var projection = new Wrath335PortalProjection();
        projection.Prepare(Matrix4x4.Identity, Matrix4x4.Identity);
        projection.ClipVolumes = volumes;
        var output = new Vector3[32];
        Assert.AreEqual(0, projection.ProjectPolygon(portal, new(0f, 0f, 1f), output, out _, Vector3.Zero));
        Assert.AreEqual(4, projection.ProjectPolygon(portal, new(0f, 0f, 1f), output, out _,
            Vector3.Zero, bypassClipVolumes: true));
        Assert.AreEqual(4, projection.ProjectPolygon(portal, new(0f, 0f, 1f), output, out _, new(0f, 0f, 0.3f)));
        projection.Prepare(Matrix4x4.CreateTranslation(0f, 0f, 0.4f), Matrix4x4.Identity);
        projection.ClipVolumes = volumes;
        Assert.AreEqual(4, projection.ProjectPolygon(portal, new(0f, 0f, 1f), output, out _, Vector3.Zero));
        projection.Prepare(Matrix4x4.Identity, Matrix4x4.Identity); // An ordinary new preparation drops old scene volumes.
        Assert.AreEqual(4, projection.ProjectPolygon(portal, new(0f, 0f, 1f), output, out _, Vector3.Zero));
    }

    [TestMethod]
    public void NativeStormwindWallHidesItsFarSideOnlyOnItsOwnMap()
    {
        StormwindCamera(out var center, out var forward, out var eye, out var camera);
        var volumes = new Wrath335ClipVolumes();
        volumes.Prepare(0, eye, forward, camera, 0f);
        Assert.AreEqual(1, volumes.Volumes.Length);
        Assert.IsTrue(volumes.ContainsSphere(new(center + forward * 20f, 1f)));
        Assert.IsFalse(volumes.ContainsSphere(new(center - forward * 20f, 1f)));
        volumes.Prepare(1, eye, forward, camera, 0f);
        Assert.IsFalse(volumes.ContainsSphere(new(center + forward * 20f, 1f)));
    }

    internal static void StormwindCamera(out Vector3 center, out Vector3 forward,
        out Vector3 eye, out Matrix4x4 camera)
    {
        center = new(-8900.625f, 577.3f, 69.435f);
        forward = Vector3.Normalize(new(-204.88f, -171.67f, 0f));
        eye = center - forward * 100f;
        camera = Matrix4x4.CreateLookAt(eye, center, Vector3.UnitZ) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 1f, 1000f);
    }

    private static Wrath335ClipVolumes SquareVolume()
    {
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, Square(2f), Vector3.UnitZ, 0f, false);
        return volumes;
    }

    private static Vector3[] Square(float z, float x = 0f) =>
        [new(x - 1f, -1f, z), new(x + 1f, -1f, z), new(x + 1f, 1f, z), new(x - 1f, 1f, z)];
}
