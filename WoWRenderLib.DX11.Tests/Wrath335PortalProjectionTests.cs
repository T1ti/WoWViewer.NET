using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalProjectionTests
{
    [TestMethod]
    public void EyeContainmentUsesTheSameAsymmetricEdgeAsTheViewerQuery()
    {
        var portal = Square(0f);
        Assert.IsTrue(Wrath335PortalProjection.ContainsEye(portal, new(0f, 1f, 0f)));
        Assert.IsFalse(Wrath335PortalProjection.ContainsEye(portal, new(0f, -1f, 0f)));
        Assert.IsTrue(Wrath335PortalProjection.ContainsEye(portal, new(0f, 0f, 0.00999f)));
        Assert.IsFalse(Wrath335PortalProjection.ContainsEye(portal, new(0f, 0f, 0.01f)));
        Assert.IsFalse(Wrath335PortalProjection.ContainsEye(portal, new(0f, 0f, -0.01f)));
    }

    [TestMethod]
    public void PlaneClassificationKeepsToleranceVerticesWithoutDuplicateIntersections()
    {
        Vector3[] source = [new(-0.0001f, 0f, 0f), new(1f, 1f, 0f), new(-1f, 2f, 0f)];
        Vector3[] output = new Vector3[32];
        var count = Wrath335PortalProjection.ClipAgainstPlane(source, output, new(1f, 0f, 0f, 0f));
        Assert.AreEqual(3, count);
        Assert.AreEqual(source[0], output[0]);
        Assert.AreEqual(source[1], output[1]);
        Assert.AreEqual(new Vector3(0f, 1.5f, 0f), output[2]);
        source[0].X = -0.000101f;
        count = Wrath335PortalProjection.ClipAgainstPlane(source, output, new(1f, 0f, 0f, 0f));
        Assert.AreEqual(3, count);
        Assert.AreEqual(0f, output[0].X, 0.00000001f);
        Assert.IsTrue(output[0].Y > 0f);
    }

    [TestMethod]
    public void FivePlanesClipFarAndLeaveTheNearPlaneOut()
    {
        var projection = new Wrath335PortalProjection();
        projection.Prepare(Matrix4x4.Identity, Matrix4x4.Identity);
        Assert.AreEqual(WmoPortalRect.Full, projection.Project(Square(-0.5f), new(0f, 0f, 2f)));
        Assert.AreEqual(default(WmoPortalRect), projection.Project(Square(1.01f), new(0f, 0f, 2f)));
    }

    [TestMethod]
    public void WorldToleranceIsIndependentOfHomogeneousMatrixScale()
    {
        var portal = Square(0.5f);
        portal = portal with { Vertices = [new(1.00005f, -0.5f, 0.5f), new(1.00005f, 0.5f, 0.5f),
            new(1.00009f, 0.5f, 0.5f), new(1.00009f, -0.5f, 0.5f)] };
        var projection = new Wrath335PortalProjection();
        projection.Prepare(Matrix4x4.Identity, Matrix4x4.Identity);
        var original = projection.Project(portal, new(0f, 0f, 2f));
        Assert.IsTrue(original.MaxX > 1f);
        projection.Prepare(Matrix4x4.Identity, Matrix4x4.Multiply(Matrix4x4.Identity, 100f));
        Assert.AreEqual(original, projection.Project(portal, new(0f, 0f, 2f)));
    }

    [TestMethod]
    public void ProjectionAppliesPlacementTransformAndCapsTheInputAtTwelveVertices()
    {
        var portal = Square(0.5f);
        portal = portal with { Vertices = Enumerable.Range(0, 12).Select(i => new Vector3(
            0.2f * MathF.Cos(i * MathF.PI / 6f), 0.2f * MathF.Sin(i * MathF.PI / 6f), 0.5f)).ToArray() };
        var projection = new Wrath335PortalProjection();
        projection.Prepare(Matrix4x4.CreateTranslation(0.1f, 0f, 0f), Matrix4x4.Identity);
        var original = projection.Project(portal, new(0f, 0f, 2f));
        Assert.AreEqual(0.3f, original.MaxX, 0.000001f);
        portal = portal with { Vertices = [.. portal.Vertices, new(100f, 100f, 0.5f)] };
        Assert.AreEqual(original, projection.Project(portal, new(0f, 0f, 2f)));
    }

    internal static WmoPortal Square(float z) => new()
    {
        Normal = Vector3.UnitZ, Distance = -z,
        Vertices = [new(-1f, -1f, z), new(1f, -1f, z), new(1f, 1f, z), new(-1f, 1f, z)]
    };
}
