using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335CloudMeshTests
{
    [TestMethod]
    public void ClientCloudCapClosesAtTheHorizonAndFadesItsOuterRings()
    {
        var vertices = Wrath335CloudMesh.CreateVertices();
        var indices = Wrath335CloudMesh.CreateStripIndices();

        Assert.AreEqual(177, vertices.Length);
        Assert.AreEqual(374, indices.Length);
        Assert.AreEqual(new Vector2(0.5f, 0.5f),
            vertices[0].TextureCoordinate);
        Assert.AreEqual((1f - Wrath335SkyReference.VerticalOffset) *
            Wrath335SkyReference.RenderScale,
            vertices[0].Position.Z, 0.00001f);

        var ninthRing = 1 + 8 * Wrath335CloudMesh.AzimuthSegments;
        var tenthRing = 1 + 9 * Wrath335CloudMesh.AzimuthSegments;
        var lastRing = 1 + 10 * Wrath335CloudMesh.AzimuthSegments;
        Assert.AreEqual(128f / 255f, vertices[ninthRing].Color.W, 0.00001f);
        Assert.AreEqual(0f, vertices[tenthRing].Color.W);
        Assert.AreEqual(0f, vertices[lastRing].Position.Z, 0.00001f);
        Assert.AreEqual(new Vector2(0.5f, 1f),
            vertices[lastRing].TextureCoordinate);
        foreach (var index in indices)
            Assert.IsTrue(index < vertices.Length);
    }

    [TestMethod]
    public void ClientStripConnectsEveryRingWithoutVisibleSeam()
    {
        var indices = Wrath335CloudMesh.CreateStripIndices();
        var visibleTriangles = 0;
        for (var cursor = 2; cursor < indices.Length; cursor++)
        {
            var a = indices[cursor - 2];
            var b = indices[cursor - 1];
            var c = indices[cursor];
            if (a != b && b != c && a != c)
                visibleTriangles++;
        }

        Assert.AreEqual(16 + 10 * 32, visibleTriangles);
        Assert.AreEqual(indices[33], indices[34]);
    }
}
