using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ScreenSelectionGeometrySmokeTests
{
    private static ScreenSelectionVolume Volume(Matrix4x4? transform = null) =>
        new(new(450), new(550), new(1000), transform ?? Matrix4x4.Identity);

    [TestMethod]
    public void RepeatedQueriesReuseBlockBoundsWithoutAllocatingAndFindLastBlockHits()
    {
        Vector3[] vertices = [new(-2, -1, .5f), new(-2, 1, .5f), new(-1, 0, .5f),
            new(-1, -1, .5f), new(1, -1, .5f), new(0, 1, .5f)];
        var indices = new ushort[30_000];
        for (var index = 0; index < indices.Length; index++) indices[index] = (ushort)(index % 3);
        var volume = Volume();
        var mesh = ScreenSelectionMesh.Get(vertices, indices);
        Assert.IsFalse(mesh.Intersects(volume));
        _ = ScreenSelectionMesh.Get(vertices, indices);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var anyHit = false;
        for (var iteration = 0; iteration < 100; iteration++)
            anyHit |= ScreenSelectionMesh.Get(vertices, indices).Intersects(volume);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsFalse(anyHit);
        Assert.AreEqual(0L, allocated);
        var withHit = (ushort[])indices.Clone();
        withHit[^3] = 3; withHit[^2] = 4; withHit[^1] = 5;
        Assert.IsTrue(ScreenSelectionMesh.Get(vertices, withHit).Intersects(volume));
    }

    [TestMethod]
    public void RejectsEmptySpaceInsideMeshBoundsAndAcceptsTriangleCrossingRectangle()
    {
        var volume = Volume();
        Vector3[] vertices = [new(-.8f, -.8f, .5f), new(.8f, -.8f, .5f), new(.8f, -.6f, .5f),
            new(-.8f, .6f, .5f), new(-.8f, .8f, .5f), new(.8f, .8f, .5f)];
        ushort[] indices = [0, 1, 2, 3, 4, 5];
        Assert.IsTrue(volume.Intersects(new BoundingBox(new(-.8f, -.8f, .5f), new(.8f, .8f, .5f))));
        var mesh = ScreenSelectionMesh.Get(vertices, indices);
        Assert.IsFalse(mesh.Intersects(volume));
        Assert.AreSame(mesh, ScreenSelectionMesh.Get(vertices, indices));
        Assert.IsTrue(volume.IntersectsTriangle(new(-1, -1, .5f), new(1, -1, .5f), new(0, 1, .5f)));
        // Triangle's AABB overlaps, but its diagonal edge misses the rectangle.
        Assert.IsFalse(volume.IntersectsTriangle(new(-1, .8f, .5f), new(.8f, 1, .5f), new(1, -.2f, .5f)));
    }

    [TestMethod]
    public void HandlesNearFarClippingTransformsAndMalformedMeshes()
    {
        var volume = Volume();
        Assert.IsFalse(volume.IntersectsTriangle(new(-1, -1, -1), new(1, -1, -1), new(0, 1, -1)));
        Assert.IsFalse(volume.IntersectsTriangle(new(-1, -1, 2), new(1, -1, 2), new(0, 1, 2)));
        Assert.IsTrue(volume.IntersectsTriangle(new(-1, -1, -.5f), new(1, -1, .5f), new(0, 1, .5f)));
        Assert.IsTrue(Volume(Matrix4x4.CreateTranslation(-10, 0, 0)).IntersectsTriangle(
            new(9, -1, .5f), new(11, -1, .5f), new(10, 1, .5f)));
        Assert.IsFalse(ScreenSelectionMesh.Get([new(float.NaN), Vector3.Zero], [0, 1, 5, 0]).Intersects(volume));
    }
}
