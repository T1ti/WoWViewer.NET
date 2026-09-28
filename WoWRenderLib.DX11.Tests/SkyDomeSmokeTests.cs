using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class SkyDomeSmokeTests
{
    [TestMethod]
    public void WrathSkyDomeClosesEveryAzimuthRing()
    {
        var vertices = SkyDomeMesh.CreateVertices();
        var indices = SkyDomeMesh.CreateTriangleIndices();

        Assert.AreEqual(122, vertices.Length);
        Assert.AreEqual(720, indices.Length);
        foreach (var index in indices)
            Assert.IsTrue(index < vertices.Length);

        // DNSky::Build's ring-1 cubic trig differs visibly from Math.Sin/Cos.
        Assert.AreEqual(0.490348f * 6.6666665f, vertices[1].Position.Y, 0.0001f);
        Assert.AreEqual((0.846252f - 0.70710678f) * 6.6666665f,
            vertices[1].Position.Z, 0.0001f);

        // Each neighboring pair, including the 23-to-0 wrap, is a shared
        // triangle edge. This is what makes a sharp change in the sampled
        // palette interpolate continuously over the sky surface.
        for (var ring = 1; ring <= 5; ring++)
        {
            for (var segment = 0; segment < 24; segment++)
            {
                var a = 1 + (ring - 1) * 24 + segment;
                var b = 1 + (ring - 1) * 24 + (segment + 1) % 24;
                Assert.AreEqual(2, CountTrianglesWithEdge(indices, a, b),
                    $"Ring {ring} has a gap at segment {segment}.");
            }
        }
    }

    [TestMethod]
    public void WrathSkyGlowBoundaryUsesSharedVertexColors()
    {
        var sky = new WorldSkyLighting(
            TopColor: new Vector3(0f, 0f, 1f),
            MiddleColor: new Vector3(0f, 1f, 0f),
            Band1Color: new Vector3(1f, 0f, 0f),
            Band2Color: Vector3.Zero,
            SmogColor: Vector3.Zero,
            FogColor: Vector3.Zero,
            HasColorData: true,
            Skyboxes: [],
            HighlightSky: true);
        var vertices = SkyDomeMesh.CreateVertices();
        SkyDomeMesh.SetColors(vertices, sky, glowStrength: 0.5f, clientSunAzimuth: 0f);

        // At this sun angle the client's azimuth curve crosses from zero to
        // negative between segments 9 and 10. Their different sampled colors
        // must be connected by triangles, instead of selected per pixel.
        const int ring2Segment9 = 1 + 24 + 9;
        const int ring2Segment10 = ring2Segment9 + 1;
        var atBoundary = vertices[ring2Segment9].Color;
        var beyondBoundary = vertices[ring2Segment10].Color;
        Assert.AreEqual(new Vector4(190f / 255f, 63f / 255f, 0f, 1f), atBoundary);
        Assert.IsTrue(Vector4.Distance(atBoundary, beyondBoundary) > 0.1f);
        Assert.AreEqual(2, CountTrianglesWithEdge(
            SkyDomeMesh.CreateTriangleIndices(), ring2Segment9, ring2Segment10));
    }

    [TestMethod]
    public void WrathSkyDomeKeepsSampledBandsWithSkyboxFlagFour()
    {
        var sky = new WorldSkyLighting(
            TopColor: new Vector3(1f, 0f, 0f),
            MiddleColor: new Vector3(0f, 1f, 0f),
            Band1Color: Vector3.Zero,
            Band2Color: Vector3.Zero,
            SmogColor: Vector3.Zero,
            FogColor: new Vector3(0f, 0f, 1f),
            HasColorData: true,
            Skyboxes: [new WorldSkyboxLayer(123,
                WorldSkyboxLayer.ColorOverrideFlag, 0.5f)],
            HighlightSky: false);
        Assert.IsTrue(sky.OverrideColorsWithFog);

        var vertices = SkyDomeMesh.CreateVertices();
        SkyDomeMesh.SetColors(vertices, sky, glowStrength: 0f, clientSunAzimuth: 0f);

        Assert.AreEqual(new Vector4(1f, 0f, 0f, 1f), vertices[0].Color);
        Assert.AreEqual(new Vector4(0f, 254f / 255f, 0f, 1f), vertices[1].Color);
        Assert.AreEqual(new Vector4(0f, 0f, 1f, 1f), vertices[^1].Color);
    }

    [TestMethod]
    public void WrathOpaqueSkyboxSuppressesDomeOnlyWhenDrawableAndNotCombining()
    {
        var skybox = new WorldSkyboxLayer(123, Flags: 0, Opacity: 1f);
        Assert.IsTrue(SkyRenderer.SuppressesProceduralSky(skybox, drawable: true));
        Assert.IsFalse(SkyRenderer.SuppressesProceduralSky(skybox, drawable: false));
        Assert.IsFalse(SkyRenderer.SuppressesProceduralSky(
            skybox with { Opacity = 0.99f }, drawable: true));
        Assert.IsFalse(SkyRenderer.SuppressesProceduralSky(
            skybox with { Flags = 0x2 }, drawable: true));
    }

    private static int CountTrianglesWithEdge(ReadOnlySpan<ushort> indices, int a, int b)
    {
        var count = 0;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var containsA = indices[i] == a || indices[i + 1] == a || indices[i + 2] == a;
            var containsB = indices[i] == b || indices[i + 1] == b || indices[i + 2] == b;
            if (containsA && containsB)
                count++;
        }
        return count;
    }
}
