using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalFogDistanceTests
{
    [TestMethod]
    public void PortalDistanceUsesThePolygonEdgeOutsideItsProjection()
    {
        var portal = Portal();
        Assert.AreEqual(10f,
            Wrath335PortalFogDistance.DistanceToPolygon(new Vector3(0f, 0f, 10f), portal),
            0.0001f);
        Assert.AreEqual(MathF.Sqrt(101f),
            Wrath335PortalFogDistance.DistanceToPolygon(new Vector3(2f, 0f, 10f), portal),
            0.0001f);
    }

    [TestMethod]
    public void PortalDistanceStopsAtClientRangeAndExteriorGroupFlags()
    {
        var wmo = new WorldModel
        {
            portals = [Portal()],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    portalLinks = [new WmoPortalLink
                    {
                        PortalIndex = 0, TargetGroupIndex = 1
                    }]
                },
                new WorldModelGroupBatches { mogiFlags = 0x8 }
            ]
        };

        Assert.IsTrue(Wrath335PortalFogDistance.TryFind(
            wmo, 0, new Vector3(0f, 0f, 10f), out var nearby));
        Assert.AreEqual(10f, nearby, 0.0001f);
        Assert.IsFalse(Wrath335PortalFogDistance.TryFind(
            wmo, 0, new Vector3(0f, 0f, 30f), out _));
    }

    [TestMethod]
    public void ConcavePortalProjectionDistinguishesInsideFromItsNotch()
    {
        var portal = new WmoPortal
        {
            Normal = Vector3.UnitZ,
            Vertices = [new(0, 0, 0), new(3, 0, 0), new(3, 1, 0),
                new(1, 1, 0), new(1, 3, 0), new(0, 3, 0)]
        };
        Assert.AreEqual(2f, Wrath335PortalFogDistance.DistanceToPolygon(new(0.5f, 2, 2), portal));
        Assert.AreEqual(MathF.Sqrt(5), Wrath335PortalFogDistance.DistanceToPolygon(new(2, 2, 2), portal));
    }

    [TestMethod]
    public void NativePlaneDistanceKeepsSourceNormalMagnitude()
    {
        var portal = Portal() with { Normal = 2 * Vector3.UnitZ };
        Assert.AreEqual(20f, Wrath335PortalFogDistance.DistanceToPolygon(new(0, 0, 10), portal));
        Assert.AreEqual(10f, Wrath335PortalFogDistance.DistanceToPolygon(new(0, 0, 10), portal, wrath335: false));
    }

    [TestMethod]
    public void NearPlaneToleranceUsesTheOriginalPointAgainstPortalEdges()
    {
        var portal = new WmoPortal
        {
            Normal = new(-1, 0, 1),
            Vertices = [new(-0.5f, -1, -0.5f), new(0.5f, -1, 0.5f),
                new(0.5f, 1, 0.5f), new(-0.5f, 1, -0.5f)]
        };
        var point = new Vector3(0.501f, 0, 0.499f);
        Assert.AreEqual(Vector3.Distance(point, new(0.5f, 0, 0.5f)),
            Wrath335PortalFogDistance.DistanceToPolygon(point, portal), 0.0000001f);
    }

    private static WmoPortal Portal() => new()
    {
        Vertices =
        [
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
        ],
        Normal = Vector3.UnitZ,
        Distance = 0f
    };
}
