using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335GlareProjectionTests
{
    [TestMethod]
    public void OcclusionAreaTracksTheVisibleDiscAndRejectsBehindCamera()
    {
        var camera = new Camera(Vector3.Zero, 0f, 20f, 16f / 9f);
        var center = camera.Front * 12f;
        var vertices = new Wrath335CelestialVertex[
            Wrath335CelestialQuad.MaxVertices];
        var count = Wrath335CelestialQuad.Build(
            vertices, center.Z, 1f, Vector4.One);
        var area = SkyRenderer.CalculateProjectedGlareArea(
            vertices.AsSpan(0, count), center, camera,
            camera.GetProjectionMatrix(), 1280, 720);

        Assert.IsTrue(area > 1f);
        Assert.AreEqual(0f, SkyRenderer.CalculateProjectedGlareArea(
            vertices.AsSpan(0, count), -center, camera,
            camera.GetProjectionMatrix(), 1280, 720));
    }
}
