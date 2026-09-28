using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335CelestialQuadTests
{
    [TestMethod]
    public void HiddenBodyProducesNoQuad()
    {
        var vertices = new Wrath335CelestialVertex[Wrath335CelestialQuad.MaxVertices];
        Assert.AreEqual(0, Wrath335CelestialQuad.Build(
            vertices, centerHeightAboveEye: -2f, fullSize: 1f, tint: Vector4.One));
    }

    [TestMethod]
    public void VisibleBodyUsesClientFullSizeAndKeepsTintAlpha()
    {
        var vertices = new Wrath335CelestialVertex[Wrath335CelestialQuad.MaxVertices];
        var tint = new Vector4(1f, 0.5f, 0.25f, 0.75f);
        var count = Wrath335CelestialQuad.Build(
            vertices, centerHeightAboveEye: 2f, fullSize: 1f, tint: tint);

        Assert.AreEqual(4, count);
        Assert.AreEqual(new Vector3(0f, -0.5f, 0.5f), vertices[0].LocalPosition);
        Assert.AreEqual(new Vector3(0f, 0.5f, -0.5f), vertices[3].LocalPosition);
        Assert.AreEqual(new Vector2(1f, 1f), vertices[3].TextureCoordinate);
        Assert.AreEqual(tint, vertices[0].Color);
        Assert.AreEqual(6, Wrath335CelestialQuad.FourVertexIndices.Length);
    }

    [TestMethod]
    public void HorizonCrossingClipsBottomAndAddsFadeRow()
    {
        var vertices = new Wrath335CelestialVertex[Wrath335CelestialQuad.MaxVertices];
        var count = Wrath335CelestialQuad.Build(
            vertices, centerHeightAboveEye: 0.1f, fullSize: 1f, tint: Vector4.One);

        Assert.AreEqual(6, count);
        Assert.AreEqual(0.3f, vertices[2].LocalPosition.Z, 0.0001f);
        Assert.AreEqual(0.2f, vertices[2].TextureCoordinate.Y, 0.0001f);
        Assert.AreEqual(1f, vertices[2].Color.W, 0.0001f);
        Assert.AreEqual(-0.1f, vertices[4].LocalPosition.Z, 0.0001f);
        Assert.AreEqual(0.6f, vertices[4].TextureCoordinate.Y, 0.0001f);
        Assert.AreEqual(0f, vertices[4].Color.W, 0.0001f);
        Assert.AreEqual(12, Wrath335CelestialQuad.SixVertexIndices.Length);
    }

    [TestMethod]
    public void ClientBillboardBasisKeepsQuadUprightAtObliqueView()
    {
        var forward = Vector3.Normalize(new Vector3(1f, 1f, 0.5f));
        Wrath335CelestialQuad.BuildCameraFacingBasis(
            forward, out var horizontal, out var vertical);

        Assert.AreEqual(0f, horizontal.Z, 0.00001f);
        Assert.AreEqual(1f, horizontal.Length(), 0.00001f);
        Assert.AreEqual(0f, Vector3.Dot(forward, horizontal), 0.00001f);
        Assert.AreEqual(0f, Vector3.Dot(forward, vertical), 0.00001f);
        Assert.IsTrue(vertical.Z > 0f);
    }

    [TestMethod]
    public void ClientBillboardUsesFixedHorizontalAxisAtCardinalView()
    {
        Wrath335CelestialQuad.BuildCameraFacingBasis(
            Vector3.UnitX, out var horizontal, out var vertical);

        Assert.AreEqual(Vector3.UnitY, horizontal);
        Assert.AreEqual(Vector3.UnitZ, vertical);
    }
}
